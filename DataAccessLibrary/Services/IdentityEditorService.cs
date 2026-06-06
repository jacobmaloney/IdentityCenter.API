using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Services;

/// <summary>
/// Consolidated service for Identity CRUD, audit logging, and AD cascade operations.
/// Fixes the data-loss bug where previous Dapper operations only saved 11-15 of 53 columns.
/// </summary>
public class IdentityEditorService : DapperRepositoryBase, IIdentityEditorService
{
    private readonly IAdminRepository _adminRepo;
    private readonly IDirectoryWriteService _directoryWriteService;
    private readonly IAuditLogService _auditLogService;
    private readonly IObjectWriteBackService? _writeBackService;
    private readonly IProcessEventPublisher? _eventPublisher;

    public IdentityEditorService(
        IConfiguration configuration,
        IGlobalLogger logger,
        IAdminRepository adminRepo,
        IDirectoryWriteService directoryWriteService,
        IAuditLogService auditLogService,
        IObjectWriteBackService? writeBackService = null,
        IProcessEventPublisher? eventPublisher = null)
        : base(configuration, logger)
    {
        _adminRepo = adminRepo;
        _directoryWriteService = directoryWriteService;
        _auditLogService = auditLogService;
        _writeBackService = writeBackService;
        _eventPublisher = eventPublisher;
    }

    public async Task<Guid> CreateIdentityAsync(Identity identity)
    {
        const string sql = @"
            INSERT INTO Identities (
                Id, CentralId, DisplayName, FirstName, LastName, MiddleName, Suffix, Salutation,
                PreferredName, DateOfBirth, Gender, NationalId, PhotoUrl,
                PrimaryEmail, SecondaryEmail, PrimaryPhone, MobilePhone, HomePhone, Fax,
                StreetAddress, City, State, PostalCode, Country,
                EmployeeId, JobTitle, Department, Division, Company, Office, Building, Floor, Room,
                CostCenter, ProfitCenter, IdentityType, ContractType, HireDate, TerminationDate, LastWorkDay, StartDate, EndDate,
                Description, ManagerIdentityId, ManagerEmployeeId,
                Username, UserPrincipalName, Status, IsActive, SecurityClearance, RiskScore, RiskLevel,
                AuthoritativeSourceId, PreferredLanguage, TimeZone, Locale,
                CreatedAt, ModifiedAt, LastSeenAt, LastLoginAt, PasswordLastChangedAt, LastAccessReviewAt,
                CreatedBy, ModifiedBy, CustomAttributes
            )
            VALUES (
                @Id, @CentralId, @DisplayName, @FirstName, @LastName, @MiddleName, @Suffix, @Salutation,
                @PreferredName, @DateOfBirth, @Gender, @NationalId, @PhotoUrl,
                @PrimaryEmail, @SecondaryEmail, @PrimaryPhone, @MobilePhone, @HomePhone, @Fax,
                @StreetAddress, @City, @State, @PostalCode, @Country,
                @EmployeeId, @JobTitle, @Department, @Division, @Company, @Office, @Building, @Floor, @Room,
                @CostCenter, @ProfitCenter, @IdentityType, @ContractType, @HireDate, @TerminationDate, @LastWorkDay, @StartDate, @EndDate,
                @Description, @ManagerIdentityId, @ManagerEmployeeId,
                @Username, @UserPrincipalName, @Status, @IsActive, @SecurityClearance, @RiskScore, @RiskLevel,
                @AuthoritativeSourceId, @PreferredLanguage, @TimeZone, @Locale,
                @CreatedAt, @ModifiedAt, @LastSeenAt, @LastLoginAt, @PasswordLastChangedAt, @LastAccessReviewAt,
                @CreatedBy, @ModifiedBy, @CustomAttributes
            )";

        if (identity.Id == Guid.Empty)
        {
            identity.Id = Guid.NewGuid();
        }

        identity.CreatedAt = DateTime.UtcNow;
        identity.ModifiedAt = DateTime.UtcNow;

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, identity);

        _logger.LogInformation(string.Concat("Created identity ", identity.Id.ToString(), " with ALL columns persisted"));

        // Record "Created" audit entry so change history shows the creation event
        await _auditLogService.LogChangeAsync(new ChangeAuditEntry
        {
            Timestamp = identity.CreatedAt,
            OperationType = ChangeOperationType.Create,
            EntityType = "Identity",
            EntityId = identity.Id,
            EntityDisplayName = identity.DisplayName,
            Source = "IdentityEditor",
            Success = true
        });

        // Publish IdentityCreated event for workflow triggers
        if (_eventPublisher != null)
        {
            _logger.LogInformation(string.Concat("Publishing IdentityCreated event for identity ", identity.Id.ToString()));
            await _eventPublisher.PublishAsync(
                DataAccessLibrary.Models.WorkflowEventType.IdentityCreated,
                identity.Id,
                "Identity",
                new Dictionary<string, object>
                {
                    { "DisplayName", identity.DisplayName ?? "" },
                    { "IdentityType", identity.IdentityType ?? "" },
                    { "Department", identity.Department ?? "" },
                    { "Status", identity.Status ?? "Active" }
                });
            _logger.LogInformation(string.Concat("IdentityCreated event published for identity ", identity.Id.ToString()));
        }
        else
        {
            _logger.LogWarning("IdentityEditorService: _eventPublisher is null — IdentityCreated event will NOT fire for workflow triggers");
        }

        return identity.Id;
    }

    public async Task UpdateIdentityAsync(Identity identity)
    {
        const string sql = @"
            UPDATE Identities SET
                CentralId = @CentralId,
                DisplayName = @DisplayName,
                FirstName = @FirstName,
                LastName = @LastName,
                MiddleName = @MiddleName,
                Suffix = @Suffix,
                Salutation = @Salutation,
                PreferredName = @PreferredName,
                DateOfBirth = @DateOfBirth,
                Gender = @Gender,
                NationalId = @NationalId,
                PhotoUrl = @PhotoUrl,
                PrimaryEmail = @PrimaryEmail,
                SecondaryEmail = @SecondaryEmail,
                PrimaryPhone = @PrimaryPhone,
                MobilePhone = @MobilePhone,
                HomePhone = @HomePhone,
                Fax = @Fax,
                StreetAddress = @StreetAddress,
                City = @City,
                State = @State,
                PostalCode = @PostalCode,
                Country = @Country,
                EmployeeId = @EmployeeId,
                JobTitle = @JobTitle,
                Department = @Department,
                Division = @Division,
                Company = @Company,
                Office = @Office,
                Building = @Building,
                Floor = @Floor,
                Room = @Room,
                CostCenter = @CostCenter,
                ProfitCenter = @ProfitCenter,
                IdentityType = @IdentityType,
                ContractType = @ContractType,
                HireDate = @HireDate,
                TerminationDate = @TerminationDate,
                LastWorkDay = @LastWorkDay,
                StartDate = @StartDate,
                EndDate = @EndDate,
                Description = @Description,
                ManagerIdentityId = @ManagerIdentityId,
                ManagerEmployeeId = @ManagerEmployeeId,
                Username = @Username,
                UserPrincipalName = @UserPrincipalName,
                Status = @Status,
                IsActive = @IsActive,
                SecurityClearance = @SecurityClearance,
                RiskScore = @RiskScore,
                RiskLevel = @RiskLevel,
                AuthoritativeSourceId = @AuthoritativeSourceId,
                PreferredLanguage = @PreferredLanguage,
                TimeZone = @TimeZone,
                Locale = @Locale,
                ModifiedAt = @ModifiedAt,
                LastSeenAt = @LastSeenAt,
                LastLoginAt = @LastLoginAt,
                PasswordLastChangedAt = @PasswordLastChangedAt,
                LastAccessReviewAt = @LastAccessReviewAt,
                ModifiedBy = @ModifiedBy,
                CustomAttributes = @CustomAttributes
            WHERE Id = @Id";

        identity.ModifiedAt = DateTime.UtcNow;

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, identity);

        _logger.LogInformation(string.Concat("Updated identity ", identity.Id.ToString(), " with ALL columns persisted"));
    }

    public async Task<IdentitySaveResult> SaveWithCascadeAsync(Identity identity, Identity? originalSnapshot, string source)
    {
        try
        {
            // 1. Save the identity to database
            await UpdateIdentityAsync(identity);

            // 2. Generate audit entries if we have an original snapshot
            if (originalSnapshot != null)
            {
                var auditEntries = GenerateAuditEntries(identity, originalSnapshot, source);
                if (auditEntries.Any())
                {
                    await _auditLogService.LogChangesAsync(auditEntries);
                }
            }

            // 3. Cascade changes to linked AD objects
            var (successCount, totalCount, failedCount) = await CascadeToLinkedObjectsAsync(identity);

            // 4. Cascade manager to linked Objects if ManagerIdentityId changed
            if (originalSnapshot != null && originalSnapshot.ManagerIdentityId != identity.ManagerIdentityId)
            {
                var (mgrSuccess, mgrTotal, mgrFailed) = await CascadeManagerToLinkedObjectsAsync(identity);
                successCount += mgrSuccess;
                totalCount += mgrTotal;
                failedCount += mgrFailed;
            }

            // 5. Build result message
            var message = failedCount > 0
                ? string.Concat("Identity saved. Updated ", successCount.ToString(), " of ", totalCount.ToString(), " linked AD accounts (", failedCount.ToString(), " failed)")
                : totalCount > 0
                    ? string.Concat("Identity saved and cascaded to ", successCount.ToString(), " linked AD accounts")
                    : "Identity saved successfully";

            return new IdentitySaveResult(true, message, successCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(string.Concat("Error saving identity ", identity.Id.ToString()), ex);
            return new IdentitySaveResult(false, string.Concat("Failed to save identity: ", ex.Message), 0, 0);
        }
    }

    private List<ChangeAuditEntry> GenerateAuditEntries(Identity current, Identity original, string source)
    {
        var entries = new List<ChangeAuditEntry>();
        var correlationId = Guid.NewGuid();

        // Helper to add audit entry if value changed
        void AddIfChanged(string propertyName, string? oldValue, string? newValue)
        {
            if (oldValue != newValue)
            {
                entries.Add(new ChangeAuditEntry
                {
                    Timestamp = DateTime.UtcNow,
                    OperationType = ChangeOperationType.Update,
                    EntityType = "Identity",
                    EntityId = current.Id,
                    PropertyName = propertyName,
                    OldValue = oldValue,
                    NewValue = newValue,
                    CorrelationId = correlationId,
                    Source = source,
                    Success = true
                });
            }
        }

        // Core biographic
        AddIfChanged("DisplayName", original.DisplayName, current.DisplayName);
        AddIfChanged("FirstName", original.FirstName, current.FirstName);
        AddIfChanged("LastName", original.LastName, current.LastName);
        AddIfChanged("MiddleName", original.MiddleName, current.MiddleName);
        AddIfChanged("Suffix", original.Suffix, current.Suffix);
        AddIfChanged("Salutation", original.Salutation, current.Salutation);
        AddIfChanged("PreferredName", original.PreferredName, current.PreferredName);
        AddIfChanged("Gender", original.Gender, current.Gender);

        // Contact
        AddIfChanged("PrimaryEmail", original.PrimaryEmail, current.PrimaryEmail);
        AddIfChanged("SecondaryEmail", original.SecondaryEmail, current.SecondaryEmail);
        AddIfChanged("PrimaryPhone", original.PrimaryPhone, current.PrimaryPhone);
        AddIfChanged("MobilePhone", original.MobilePhone, current.MobilePhone);
        AddIfChanged("HomePhone", original.HomePhone, current.HomePhone);
        AddIfChanged("Fax", original.Fax, current.Fax);
        AddIfChanged("StreetAddress", original.StreetAddress, current.StreetAddress);
        AddIfChanged("City", original.City, current.City);
        AddIfChanged("State", original.State, current.State);
        AddIfChanged("PostalCode", original.PostalCode, current.PostalCode);
        AddIfChanged("Country", original.Country, current.Country);

        // Organizational
        AddIfChanged("EmployeeId", original.EmployeeId, current.EmployeeId);
        AddIfChanged("JobTitle", original.JobTitle, current.JobTitle);
        AddIfChanged("Department", original.Department, current.Department);
        AddIfChanged("Division", original.Division, current.Division);
        AddIfChanged("Company", original.Company, current.Company);
        AddIfChanged("Office", original.Office, current.Office);
        AddIfChanged("Building", original.Building, current.Building);
        AddIfChanged("Floor", original.Floor, current.Floor);
        AddIfChanged("Room", original.Room, current.Room);
        AddIfChanged("CostCenter", original.CostCenter, current.CostCenter);
        AddIfChanged("ProfitCenter", original.ProfitCenter, current.ProfitCenter);
        AddIfChanged("IdentityType", original.IdentityType, current.IdentityType);
        AddIfChanged("ContractType", original.ContractType, current.ContractType);
        AddIfChanged("Description", original.Description, current.Description);
        AddIfChanged("ManagerIdentityId", original.ManagerIdentityId?.ToString(), current.ManagerIdentityId?.ToString());

        // Technical
        AddIfChanged("Username", original.Username, current.Username);
        AddIfChanged("UserPrincipalName", original.UserPrincipalName, current.UserPrincipalName);
        AddIfChanged("Status", original.Status, current.Status);
        AddIfChanged("IsActive", original.IsActive.ToString(), current.IsActive.ToString());
        AddIfChanged("SecurityClearance", original.SecurityClearance, current.SecurityClearance);
        AddIfChanged("RiskScore", original.RiskScore?.ToString(), current.RiskScore?.ToString());
        AddIfChanged("RiskLevel", original.RiskLevel, current.RiskLevel);

        // Localization
        AddIfChanged("PreferredLanguage", original.PreferredLanguage, current.PreferredLanguage);
        AddIfChanged("TimeZone", original.TimeZone, current.TimeZone);
        AddIfChanged("Locale", original.Locale, current.Locale);

        return entries;
    }

    private async Task<(int successCount, int totalCount, int failedCount)> CascadeToLinkedObjectsAsync(Identity identity)
    {
        try
        {
            // Find all linked user objects for this identity
            var allObjects = await _adminRepo.GetObjectsAsync(objectClass: "user");
            var linkedObjects = allObjects.Where(o => o.IdentityId == identity.Id).ToList();

            if (!linkedObjects.Any())
            {
                _logger.LogInformation(string.Concat("No linked user objects found for identity ", identity.Id.ToString(), " - skipping cascade"));
                return (0, 0, 0);
            }

            _logger.LogInformation(string.Concat("Cascading identity edit to ", linkedObjects.Count.ToString(), " linked objects for ", identity.Id.ToString()));

            // Build fields dictionary from Identity values (all fields the WriteBackService supports)
            var fields = new Dictionary<string, string?>();
            if (identity.DisplayName != null) fields["DisplayName"] = identity.DisplayName;
            if (identity.FirstName != null) fields["FirstName"] = identity.FirstName;
            if (identity.LastName != null) fields["LastName"] = identity.LastName;
            if (identity.PrimaryEmail != null) fields["Email"] = identity.PrimaryEmail;
            if (identity.PrimaryPhone != null) fields["Phone"] = identity.PrimaryPhone;
            if (identity.Department != null) fields["Department"] = identity.Department;
            if (identity.JobTitle != null) fields["JobTitle"] = identity.JobTitle;
            if (identity.MiddleName != null) fields["MiddleName"] = identity.MiddleName;
            if (identity.MobilePhone != null) fields["MobilePhone"] = identity.MobilePhone;
            if (identity.HomePhone != null) fields["HomePhone"] = identity.HomePhone;
            if (identity.Fax != null) fields["Fax"] = identity.Fax;
            if (identity.StreetAddress != null) fields["StreetAddress"] = identity.StreetAddress;
            if (identity.City != null) fields["City"] = identity.City;
            if (identity.State != null) fields["State"] = identity.State;
            if (identity.PostalCode != null) fields["PostalCode"] = identity.PostalCode;
            if (identity.Country != null) fields["Country"] = identity.Country;
            if (identity.Company != null) fields["Company"] = identity.Company;
            if (identity.Division != null) fields["Division"] = identity.Division;
            if (identity.Office != null) fields["Office"] = identity.Office;
            if (identity.EmployeeId != null) fields["EmployeeId"] = identity.EmployeeId;
            if (identity.Description != null) fields["Description"] = identity.Description;
            if (identity.Username != null) fields["Username"] = identity.Username;
            if (identity.UserPrincipalName != null) fields["UserPrincipalName"] = identity.UserPrincipalName;

            int successCount = 0;
            int failCount = 0;

            foreach (var obj in linkedObjects)
            {
                try
                {
                    if (_writeBackService != null && fields.Count > 0)
                    {
                        // Centralized write-back: handles DB + AD + audit in one call with timeout
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        try
                        {
                            var result = await _writeBackService.UpdateFieldsAsync(obj.Id, fields, "IdentityCascade");
                            if (result.Success)
                            {
                                _logger.LogInformation(string.Concat("Cascaded changes to AD for object ", obj.Id.ToString()));
                                successCount++;
                            }
                            else
                            {
                                _logger.LogWarning(string.Concat("Cascade partial for object ", obj.Id.ToString(), ": ", string.Join("; ", result.Errors)));
                                if (result.DatabaseUpdated) successCount++;
                                else failCount++;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning(string.Concat("Write-back timed out for object ", obj.Id.ToString()));
                            failCount++;
                        }
                    }
                    else
                    {
                        // Fallback: manual DB update + direct AD write
                        obj.DisplayName = identity.DisplayName;
                        obj.FirstName = identity.FirstName;
                        obj.LastName = identity.LastName;
                        obj.Email = identity.PrimaryEmail;
                        obj.Phone = identity.PrimaryPhone;
                        obj.Department = identity.Department;
                        obj.JobTitle = identity.JobTitle;
                        obj.IsActive = identity.IsActive;
                        obj.LastSyncedAt = DateTime.UtcNow;
                        await _adminRepo.UpdateObjectAsync(obj);

                        var attributes = new Dictionary<string, string>();
                        if (!string.IsNullOrEmpty(identity.DisplayName)) attributes["displayname"] = identity.DisplayName;
                        if (!string.IsNullOrEmpty(identity.FirstName)) attributes["firstname"] = identity.FirstName;
                        if (!string.IsNullOrEmpty(identity.LastName)) attributes["lastname"] = identity.LastName;
                        if (!string.IsNullOrEmpty(identity.PrimaryEmail)) attributes["email"] = identity.PrimaryEmail;
                        if (!string.IsNullOrEmpty(identity.PrimaryPhone)) attributes["phone"] = identity.PrimaryPhone;
                        if (!string.IsNullOrEmpty(identity.Department)) attributes["department"] = identity.Department;
                        if (!string.IsNullOrEmpty(identity.JobTitle)) attributes["jobtitle"] = identity.JobTitle;

                        if (attributes.Any())
                        {
                            var adTask = _directoryWriteService.UpdateUserAsync(obj.Id, attributes);
                            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                            var completedTask = await Task.WhenAny(adTask, timeoutTask);
                            if (completedTask == timeoutTask) { failCount++; }
                            else { var adSuccess = await adTask; if (adSuccess) successCount++; else failCount++; }
                        }
                        else { successCount++; }
                    }
                }
                catch (Exception objEx)
                {
                    _logger.LogError(string.Concat("Error cascading to object ", obj.Id.ToString()), objEx);
                    failCount++;
                }
            }

            return (successCount, linkedObjects.Count, failCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(string.Concat("Error in cascade update for identity ", identity.Id.ToString()), ex);
            return (0, 0, 1);
        }
    }

    /// <summary>
    /// Reverse manager cascade: when an Identity's ManagerIdentityId changes,
    /// update ManagerObjectId on all linked Objects. For each Object, finds the
    /// new manager's Object in the same SourceConnectionId (same AD forest).
    /// </summary>
    private async Task<(int successCount, int totalCount, int failedCount)> CascadeManagerToLinkedObjectsAsync(Identity identity)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Find all linked user objects for this identity
            var linkedObjects = (await connection.QueryAsync<(Guid Id, Guid? SourceConnectionId, string? DN)>(
                @"SELECT Id, SourceConnectionId, DN FROM Objects
                  WHERE IdentityId = @IdentityId AND ObjectClass = 'user'",
                new { IdentityId = identity.Id })).ToList();

            if (linkedObjects.Count == 0)
            {
                _logger.LogInformation(string.Concat("No linked user objects for identity ", identity.Id.ToString(), " - skipping manager cascade"));
                return (0, 0, 0);
            }

            _logger.LogInformation(string.Concat("Cascading manager change to ", linkedObjects.Count.ToString(), " linked objects for identity ", identity.Id.ToString()));

            int successCount = 0;
            int failCount = 0;

            if (identity.ManagerIdentityId == null)
            {
                // Manager cleared - clear ManagerObjectId on all linked Objects
                foreach (var obj in linkedObjects)
                {
                    try
                    {
                        if (_writeBackService != null)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            try
                            {
                                var result = await _writeBackService.SetObjectManagerAsync(obj.Id, null, null, "IdentityManagerCascade");
                                if (result.Success || result.DatabaseUpdated) successCount++;
                                else failCount++;
                            }
                            catch (OperationCanceledException)
                            {
                                _logger.LogWarning(string.Concat("Manager cascade timed out for object ", obj.Id.ToString()));
                                failCount++;
                            }
                        }
                        else
                        {
                            await connection.ExecuteAsync(
                                "UPDATE Objects SET ManagerObjectId = NULL, ModifiedAt = @Now WHERE Id = @Id",
                                new { Id = obj.Id, Now = DateTime.UtcNow });
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(string.Concat("Error clearing manager on object ", obj.Id.ToString()), ex);
                        failCount++;
                    }
                }
            }
            else
            {
                // Manager set - find the manager's Object in the same forest as each linked Object
                // First, get all Objects linked to the new manager Identity
                var managerObjects = (await connection.QueryAsync<(Guid Id, Guid? SourceConnectionId, string? DN)>(
                    @"SELECT Id, SourceConnectionId, DN FROM Objects
                      WHERE IdentityId = @ManagerIdentityId AND ObjectClass = 'user'",
                    new { ManagerIdentityId = identity.ManagerIdentityId })).ToList();

                foreach (var obj in linkedObjects)
                {
                    try
                    {
                        // Find the manager's Object in the same SourceConnectionId
                        var managerInSameForest = managerObjects.FirstOrDefault(m => m.SourceConnectionId == obj.SourceConnectionId);

                        if (managerInSameForest.Id == Guid.Empty)
                        {
                            _logger.LogWarning(string.Concat("Manager Identity has no Object in SourceConnectionId ",
                                (obj.SourceConnectionId?.ToString() ?? "null"), " for object ", obj.Id.ToString(), " - skipping"));
                            failCount++;
                            continue;
                        }

                        if (_writeBackService != null)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            try
                            {
                                var result = await _writeBackService.SetObjectManagerAsync(
                                    obj.Id, managerInSameForest.DN, managerInSameForest.Id, "IdentityManagerCascade");
                                if (result.Success || result.DatabaseUpdated)
                                {
                                    _logger.LogInformation(string.Concat("Cascaded manager to object ", obj.Id.ToString(),
                                        " -> manager ", managerInSameForest.Id.ToString()));
                                    successCount++;
                                }
                                else
                                {
                                    _logger.LogWarning(string.Concat("Manager cascade partial for object ", obj.Id.ToString(),
                                        ": ", string.Join("; ", result.Errors)));
                                    failCount++;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                _logger.LogWarning(string.Concat("Manager cascade timed out for object ", obj.Id.ToString()));
                                failCount++;
                            }
                        }
                        else
                        {
                            await connection.ExecuteAsync(
                                "UPDATE Objects SET ManagerObjectId = @ManagerObjectId, ModifiedAt = @Now WHERE Id = @Id",
                                new { Id = obj.Id, ManagerObjectId = managerInSameForest.Id, Now = DateTime.UtcNow });
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(string.Concat("Error cascading manager to object ", obj.Id.ToString()), ex);
                        failCount++;
                    }
                }
            }

            return (successCount, linkedObjects.Count, failCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(string.Concat("Error in manager cascade for identity ", identity.Id.ToString()), ex);
            return (0, 0, 1);
        }
    }
}
