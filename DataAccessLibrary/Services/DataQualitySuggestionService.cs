using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Services;

public class DataQualitySuggestionService : IDataQualitySuggestionService
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    // Fields to check, with display labels, icons, and importance weight
    private static readonly FieldCheck[] FieldChecks = new[]
    {
        new FieldCheck("Email",            "Email",            "fa-envelope",     10),
        new FieldCheck("Phone",            "Phone",            "fa-phone",         6),
        new FieldCheck("MobilePhone",      "Mobile Phone",     "fa-mobile",        5),
        new FieldCheck("Department",       "Department",       "fa-building",      9),
        new FieldCheck("JobTitle",         "Job Title",        "fa-briefcase",     8),
        new FieldCheck("ManagerObjectId",  "Manager",          "fa-user-tie",      9),
        new FieldCheck("City",             "City",             "fa-map-marker-alt",4),
        new FieldCheck("State",            "State",            "fa-map",           3),
        new FieldCheck("Country",          "Country",          "fa-globe",         3),
        new FieldCheck("EmployeeId",       "Employee ID",      "fa-id-badge",      7),
        new FieldCheck("UserPrincipalName","UPN",              "fa-at",            8),
        new FieldCheck("Company",          "Company",          "fa-building",      5),
        new FieldCheck("Office",           "Office",           "fa-door-open",     4),
    };

    // Minimum peer group size to produce meaningful suggestions
    private const int MinPeerGroupSize = 5;

    // Minimum peer percentage to surface a suggestion
    private const int MinPeerPercent = 60;

    public DataQualitySuggestionService(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    public async Task<List<DataQualitySuggestion>> GetSuggestionsAsync(Guid objectId, CancellationToken ct = default)
    {
        var suggestions = new List<DataQualitySuggestion>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Get the target object
            var obj = await conn.QuerySingleOrDefaultAsync<ObjectSnapshot>(@"
                SELECT Id, ObjectClass, Department, Email, Phone, MobilePhone,
                       JobTitle, ManagerObjectId, City, State, Country,
                       EmployeeId, UserPrincipalName, Company, Office
                FROM Objects WHERE Id = @objectId AND DeletedAt IS NULL",
                new { objectId });

            if (obj == null) return suggestions;

            // Only check users — other object types don't have these fields meaningfully
            if (obj.ObjectClass != "user") return suggestions;

            // Build the peer comparison query dynamically
            // Peers = same ObjectClass, optionally same Department (if set)
            var peerWhere = "ObjectClass = @ObjectClass AND DeletedAt IS NULL AND IsActive = 1 AND Id != @Id";
            var peerParams = new DynamicParameters();
            peerParams.Add("ObjectClass", obj.ObjectClass);
            peerParams.Add("Id", objectId);

            if (!string.IsNullOrEmpty(obj.Department))
            {
                peerWhere += " AND Department = @Department";
                peerParams.Add("Department", obj.Department);
            }

            // Count total peers
            var peerCount = await conn.QuerySingleAsync<int>(
                $"SELECT COUNT(*) FROM Objects WHERE {peerWhere}", peerParams);

            if (peerCount < MinPeerGroupSize)
            {
                // Broaden to all users if department group is too small
                peerWhere = "ObjectClass = @ObjectClass AND DeletedAt IS NULL AND IsActive = 1 AND Id != @Id";
                peerParams = new DynamicParameters();
                peerParams.Add("ObjectClass", obj.ObjectClass);
                peerParams.Add("Id", objectId);
                peerCount = await conn.QuerySingleAsync<int>(
                    $"SELECT COUNT(*) FROM Objects WHERE {peerWhere}", peerParams);
            }

            if (peerCount < MinPeerGroupSize) return suggestions;

            // For each field, check if this object is missing it but peers have it
            foreach (var field in FieldChecks)
            {
                var currentValue = GetFieldValue(obj, field.ColumnName);
                if (!string.IsNullOrEmpty(currentValue)) continue; // field is populated, skip

                // Count peers with this field populated
                var populatedCount = await conn.QuerySingleAsync<int>(
                    $"SELECT COUNT(*) FROM Objects WHERE {peerWhere} AND {field.ColumnName} IS NOT NULL AND {field.ColumnName} != ''",
                    peerParams);

                var peerPercent = peerCount > 0 ? (int)((float)populatedCount / peerCount * 100) : 0;
                if (peerPercent < MinPeerPercent) continue;

                var peerGroup = !string.IsNullOrEmpty(obj.Department) ? $"{obj.Department} users" : "users";
                var severity = peerPercent >= 90 ? "high" : peerPercent >= 75 ? "medium" : "low";
                var priority = field.Weight * peerPercent / 10;

                suggestions.Add(new DataQualitySuggestion
                {
                    FieldName = field.ColumnName,
                    FieldLabel = field.Label,
                    Icon = field.Icon,
                    PeerPercent = peerPercent,
                    PeerCount = peerCount,
                    Priority = priority,
                    Severity = severity,
                    Message = $"{peerPercent}% of {peerGroup} have {field.Label} set"
                });
            }

            // Sort by priority descending (most important first)
            suggestions.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DataQualitySuggestionService: failed for object {ObjectId}", objectId);
        }

        return suggestions;
    }

    private static string? GetFieldValue(ObjectSnapshot obj, string fieldName) => fieldName switch
    {
        "Email" => obj.Email,
        "Phone" => obj.Phone,
        "MobilePhone" => obj.MobilePhone,
        "Department" => obj.Department,
        "JobTitle" => obj.JobTitle,
        "ManagerObjectId" => obj.ManagerObjectId?.ToString(),
        "City" => obj.City,
        "State" => obj.State,
        "Country" => obj.Country,
        "EmployeeId" => obj.EmployeeId,
        "UserPrincipalName" => obj.UserPrincipalName,
        "Company" => obj.Company,
        "Office" => obj.Office,
        _ => null
    };

    private class ObjectSnapshot
    {
        public Guid Id { get; set; }
        public string? ObjectClass { get; set; }
        public string? Department { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? MobilePhone { get; set; }
        public string? JobTitle { get; set; }
        public Guid? ManagerObjectId { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string? EmployeeId { get; set; }
        public string? UserPrincipalName { get; set; }
        public string? Company { get; set; }
        public string? Office { get; set; }
    }

    private record FieldCheck(string ColumnName, string Label, string Icon, int Weight);
}
