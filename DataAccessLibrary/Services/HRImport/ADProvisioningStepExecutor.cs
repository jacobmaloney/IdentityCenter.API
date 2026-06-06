using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services.HRImport;

/// <summary>
/// Interface for AD account creation. Defined in DataAccessLibrary to avoid
/// circular dependency with ConnectionService. Implemented by DirectoryWriteService.
/// </summary>
public interface IADAccountProvisioner
{
    Task<Guid?> CreateUserAsync(
        Guid connectionId,
        string targetOU,
        string samAccountName,
        string userPrincipalName,
        string displayName,
        Dictionary<string, string> attributes,
        string password,
        bool enableAccount = true);
}

/// <summary>
/// Executes PersonToObjectProvisionAD step: creates AD user accounts from Object records
/// that were created from Identity data (HR import) but not yet provisioned in AD.
/// Separate from InternalSyncStepExecutor to keep that service DB-only.
/// </summary>
public class ADProvisioningStepExecutor
{
    private readonly IADAccountProvisioner _writeService;
    private readonly ILogger<ADProvisioningStepExecutor> _logger;
    private readonly string _connectionString;

    public ADProvisioningStepExecutor(
        IADAccountProvisioner writeService,
        ILogger<ADProvisioningStepExecutor> logger,
        IConfiguration configuration)
    {
        _writeService = writeService;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    /// <summary>
    /// Find Objects with IdentityId set but no DN (created from Identity, not yet in AD),
    /// and provision AD accounts for them.
    /// </summary>
    public async Task<ADProvisioningResult> ExecuteAsync(
        Guid targetConnectionId,
        string targetOU,
        string? upnSuffix,
        string samAccountNamePattern,
        string? defaultPassword,
        bool enableAccounts,
        bool continueOnError,
        CancellationToken ct = default)
    {
        var result = new ADProvisioningResult();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Find objects linked to identities but not yet provisioned in AD
        var unprovisionedObjects = (await conn.QueryAsync<UnprovisionedObject>(
            @"SELECT o.Id AS ObjectId, o.SourceConnectionId,
                     i.Id AS IdentityId, i.FirstName, i.LastName, i.DisplayName,
                     i.PrimaryEmail, i.EmployeeId, i.JobTitle, i.Department,
                     i.Company, i.Office, i.PrimaryPhone, i.MobilePhone
              FROM Objects o
              INNER JOIN Identities i ON o.IdentityId = i.Id
              WHERE o.DN IS NULL
                AND o.SourceConnectionId = @ConnectionId
                AND i.IsActive = 1
              ORDER BY i.LastName, i.FirstName",
            new { ConnectionId = targetConnectionId })).ToList();

        _logger.LogInformation("AD Provisioning: Found {Count} unprovisioned objects for connection {ConnectionId}",
            unprovisionedObjects.Count, targetConnectionId);

        result.TotalToProvision = unprovisionedObjects.Count;

        foreach (var obj in unprovisionedObjects)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Generate sAMAccountName
                var samAccountName = GenerateSamAccountName(
                    obj.FirstName, obj.LastName, samAccountNamePattern);

                // Ensure uniqueness by checking AD/local DB
                samAccountName = await EnsureUniqueSamAsync(conn, samAccountName, ct);

                // Build UPN
                var upn = $"{samAccountName}@{upnSuffix ?? "corp.local"}";

                // Build display name
                var displayName = obj.DisplayName ?? $"{obj.FirstName} {obj.LastName}".Trim();

                // Additional attributes
                var attributes = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(obj.FirstName)) attributes["givenName"] = obj.FirstName;
                if (!string.IsNullOrEmpty(obj.LastName)) attributes["sn"] = obj.LastName;
                if (!string.IsNullOrEmpty(obj.PrimaryEmail)) attributes["mail"] = obj.PrimaryEmail;
                if (!string.IsNullOrEmpty(obj.JobTitle)) attributes["title"] = obj.JobTitle;
                if (!string.IsNullOrEmpty(obj.Department)) attributes["department"] = obj.Department;
                if (!string.IsNullOrEmpty(obj.Company)) attributes["company"] = obj.Company;
                if (!string.IsNullOrEmpty(obj.Office)) attributes["physicalDeliveryOfficeName"] = obj.Office;
                if (!string.IsNullOrEmpty(obj.PrimaryPhone)) attributes["telephoneNumber"] = obj.PrimaryPhone;
                if (!string.IsNullOrEmpty(obj.MobilePhone)) attributes["mobile"] = obj.MobilePhone;
                if (!string.IsNullOrEmpty(obj.EmployeeId)) attributes["employeeID"] = obj.EmployeeId;

                // Generate password
                var password = defaultPassword ?? GeneratePassword();

                // Create in AD
                var adGuid = await _writeService.CreateUserAsync(
                    targetConnectionId, targetOU, samAccountName, upn,
                    displayName, attributes, password, enableAccounts);

                if (adGuid.HasValue)
                {
                    // Update Object record with AD info
                    var dn = $"CN={displayName.Replace(",", "\\,")},{targetOU}";
                    await conn.ExecuteAsync(
                        @"UPDATE Objects SET
                            SourceUniqueId = @SourceUniqueId,
                            DN = @DN, CN = @CN,
                            DisplayName = @DisplayName,
                            Email = @Email,
                            Username = @Username,
                            ModifiedAt = SYSUTCDATETIME()
                          WHERE Id = @ObjectId",
                        new
                        {
                            ObjectId = obj.ObjectId,
                            SourceUniqueId = adGuid.Value.ToString(),
                            DN = dn,
                            CN = displayName,
                            DisplayName = displayName,
                            Email = obj.PrimaryEmail,
                            Username = samAccountName
                        });

                    result.Provisioned++;
                    _logger.LogInformation("Provisioned AD account for {DisplayName} (sAM={SAM})",
                        displayName, samAccountName);
                }
                else
                {
                    result.Errors++;
                    result.ErrorDetails.Add($"{displayName}: AD creation returned null");

                    if (!continueOnError)
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorDetails.Add($"{obj.DisplayName ?? obj.FirstName}: {ex.Message}");
                _logger.LogError(ex, "Failed to provision AD account for object {ObjectId}", obj.ObjectId);

                if (!continueOnError)
                    break;
            }
        }

        _logger.LogInformation("AD Provisioning complete: {Provisioned}/{Total} provisioned, {Errors} errors",
            result.Provisioned, result.TotalToProvision, result.Errors);

        return result;
    }

    private static string GenerateSamAccountName(string? firstName, string? lastName, string pattern)
    {
        var fn = (firstName ?? "").Trim();
        var ln = (lastName ?? "").Trim();

        var sam = pattern?.ToLowerInvariant() switch
        {
            "firstinitial_lastname" or "firstinitiallastname" =>
                (fn.Length > 0 ? fn[0].ToString() : "") + ln,
            "firstname_lastname" or "firstnamelastname" =>
                fn + ln,
            "firstname.lastname" =>
                fn + "." + ln,
            "firstinitial.lastname" =>
                (fn.Length > 0 ? fn[0].ToString() : "") + "." + ln,
            _ => // Default: FirstName.LastName
                fn + "." + ln
        };

        // Sanitize: remove invalid chars, limit to 20 chars
        sam = new string(sam.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_').ToArray());
        if (sam.Length > 20) sam = sam[..20];

        return sam.ToLowerInvariant();
    }

    private static async Task<string> EnsureUniqueSamAsync(SqlConnection conn, string baseSam, CancellationToken ct)
    {
        var sam = baseSam;
        int suffix = 1;

        while (true)
        {
            var exists = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM Objects WHERE Username = @Sam",
                new { Sam = sam });

            if (exists == 0)
                return sam;

            sam = baseSam.Length > 17 ? baseSam[..17] + suffix : baseSam + suffix;
            suffix++;

            if (suffix > 99) // Safety limit
                return baseSam + Guid.NewGuid().ToString("N")[..4];
        }
    }

    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%&*";

        var random = Random.Shared;
        var password = new char[16];

        // Ensure at least one of each type
        password[0] = upper[random.Next(upper.Length)];
        password[1] = lower[random.Next(lower.Length)];
        password[2] = digits[random.Next(digits.Length)];
        password[3] = special[random.Next(special.Length)];

        var allChars = upper + lower + digits + special;
        for (int i = 4; i < password.Length; i++)
            password[i] = allChars[random.Next(allChars.Length)];

        // Shuffle
        for (int i = password.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }

    private class UnprovisionedObject
    {
        public Guid ObjectId { get; set; }
        public Guid SourceConnectionId { get; set; }
        public Guid IdentityId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }
        public string? PrimaryEmail { get; set; }
        public string? EmployeeId { get; set; }
        public string? JobTitle { get; set; }
        public string? Department { get; set; }
        public string? Company { get; set; }
        public string? Office { get; set; }
        public string? PrimaryPhone { get; set; }
        public string? MobilePhone { get; set; }
    }
}

public class ADProvisioningResult
{
    public int TotalToProvision { get; set; }
    public int Provisioned { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorDetails { get; set; } = new();
    public bool Success => Errors == 0;
}
