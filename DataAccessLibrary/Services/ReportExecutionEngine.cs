using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("IdentityCenter.Tests")]

namespace DataAccessLibrary.Services;

/// <summary>
/// Report execution result returned by the engine.
/// </summary>
public class ReportResult
{
    public List<string> Headers { get; set; } = new();
    public List<List<string?>> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int DurationMs { get; set; }
    public bool IsTruncated { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Report definition used by the visual builder. Serialized to JSON in SavedReports.DefinitionJson or Reports.ConfigurationJson.
/// </summary>
public class VisualReportDefinition
{
    public string DataSource { get; set; } = "Objects";
    public string? ObjectClassFilter { get; set; }
    public List<VisualReportColumn> Columns { get; set; } = new();
    public List<VisualReportFilter> Filters { get; set; } = new();
    public List<VisualReportSort> SortBy { get; set; } = new();
    public int? MaxRows { get; set; }
    public bool IncludeInactive { get; set; } = false;
}

public class VisualReportColumn
{
    public string Field { get; set; } = "";
    public string Label { get; set; } = "";
    public string FieldType { get; set; } = "string";
    public bool IsAttribute { get; set; } = false;
    public string? Format { get; set; }
    public int Order { get; set; }
}

public class VisualReportFilter
{
    public string Field { get; set; } = "";
    public bool IsAttribute { get; set; } = false;
    public string Operator { get; set; } = "equals";
    public string? Value { get; set; }
}

public class VisualReportSort
{
    public string Field { get; set; } = "";
    public bool IsAttribute { get; set; } = false;
    public string Direction { get; set; } = "asc";
}

/// <summary>
/// Safely executes reports by dynamically building SQL from column/filter definitions.
/// NEVER executes raw user SQL. All field names are whitelisted or validated.
/// </summary>
public interface IReportExecutionEngine
{
    Task<ReportResult> ExecuteAsync(VisualReportDefinition definition, int? previewLimit = null, CancellationToken ct = default);
    Dictionary<string, string> GetCoreFieldMap();
    Dictionary<string, string> GetCoreFieldMap(string dataSource);
    List<string> GetCommonAttributes();
}

public class ReportExecutionEngine : IReportExecutionEngine
{
    private readonly string _connectionString;
    private readonly ILogger<ReportExecutionEngine> _logger;

    // Safe field name pattern - only allow alphanumeric, hyphen, underscore
    private static readonly Regex SafeFieldNameRegex = new(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);

    // Dangerous SQL patterns to block in any user-provided value
    private static readonly string[] DangerousPatterns = {
        "DROP", "DELETE", "UPDATE", "INSERT", "TRUNCATE", "EXEC", "EXECUTE",
        "ALTER", "CREATE", "xp_", "sp_", "--", "/*", "*/", "OPENROWSET",
        "OPENQUERY", "OPENDATASOURCE", "BULK", "SHUTDOWN"
    };

    // ----------------------------------------------------------------------
    // Per-DataSource allow-lists
    //
    // Two parallel maps per source:
    //   SelectMaps[ds]  = user-facing column -> SQL expression formatted for SELECT
    //                     (CASE/FORMAT wrappers for booleans + dates)
    //   FilterMaps[ds]  = user-facing column -> raw SQL expression for WHERE/ORDER BY
    //                     (no formatting; lets operators compare against the underlying value)
    //
    // The legacy "Objects" DataSource preserves the broad union allow-list so seeded
    // reports built before the per-source split keep working.
    // ----------------------------------------------------------------------

    // -------- Users (Objects table, ObjectClass='user') --------
    private static readonly Dictionary<string, string> UsersSelectMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"]            = "o.DisplayName",
        ["Email"]                  = "o.Email",
        ["Department"]             = "o.Department",
        ["JobTitle"]               = "o.JobTitle",
        ["Manager"]                = "mgr.DisplayName",
        ["LastLogon"]              = "FORMAT(o.LastSeenAt, 'yyyy-MM-dd HH:mm')",
        ["SourceType"]             = "o.SourceType",
        ["OriginalSource"]         = "o.OriginalSource",
        ["CreatedAt"]              = "FORMAT(o.FirstSyncedAt, 'yyyy-MM-dd HH:mm')",
        ["ObjectClass"]            = "o.ObjectClass",
        ["IsEnabled"]              = "CASE WHEN o.IsActive = 1 THEN 'Yes' ELSE 'No' END",
        ["PasswordNeverExpires"]   = "CASE WHEN o.PasswordNeverExpires = 1 THEN 'Yes' ELSE 'No' END"
    };
    private static readonly Dictionary<string, string> UsersFilterMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"]            = "o.DisplayName",
        ["Email"]                  = "o.Email",
        ["Department"]             = "o.Department",
        ["JobTitle"]               = "o.JobTitle",
        ["Manager"]                = "mgr.DisplayName",
        ["LastLogon"]              = "o.LastSeenAt",
        ["SourceType"]             = "o.SourceType",
        ["OriginalSource"]         = "o.OriginalSource",
        ["CreatedAt"]              = "o.FirstSyncedAt",
        ["ObjectClass"]            = "o.ObjectClass",
        ["IsEnabled"]              = "o.IsActive",
        ["PasswordNeverExpires"]   = "o.PasswordNeverExpires"
    };

    // -------- Groups (Objects table, ObjectClass='group') --------
    // No user-only attributes -- intentionally omits PasswordNeverExpires, JobTitle, etc.
    private static readonly Dictionary<string, string> GroupsSelectMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"]   = "o.DisplayName",
        ["Description"]   = "o.Description",
        ["SourceType"]    = "o.SourceType",
        ["OriginalSource"] = "o.OriginalSource",
        ["CreatedAt"]     = "FORMAT(o.FirstSyncedAt, 'yyyy-MM-dd HH:mm')",
        ["ObjectClass"]   = "o.ObjectClass"
    };
    private static readonly Dictionary<string, string> GroupsFilterMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"]   = "o.DisplayName",
        ["Description"]   = "o.Description",
        ["SourceType"]    = "o.SourceType",
        ["OriginalSource"] = "o.OriginalSource",
        ["CreatedAt"]     = "o.FirstSyncedAt",
        ["ObjectClass"]   = "o.ObjectClass"
    };

    // -------- Computers (Objects table, ObjectClass='computer') --------
    // OperatingSystem lives in ObjectAttributes (LDAP attr), not on Objects directly --
    // we expose it via a correlated subquery so consumers can SELECT/filter on it.
    private const string ComputerOsSubquery =
        "(SELECT TOP 1 oa.AttributeValue FROM ObjectAttributes oa WHERE oa.ObjectId = o.Id AND oa.AttributeName = 'operatingSystem')";
    private static readonly Dictionary<string, string> ComputersSelectMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"]      = "o.DisplayName",
        ["OperatingSystem"]  = ComputerOsSubquery,
        ["LastLogon"]        = "FORMAT(o.LastSeenAt, 'yyyy-MM-dd HH:mm')",
        ["SourceType"]       = "o.SourceType",
        ["OriginalSource"]   = "o.OriginalSource",
        ["CreatedAt"]        = "FORMAT(o.FirstSyncedAt, 'yyyy-MM-dd HH:mm')"
    };
    private static readonly Dictionary<string, string> ComputersFilterMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"]      = "o.DisplayName",
        ["OperatingSystem"]  = ComputerOsSubquery,
        ["LastLogon"]        = "o.LastSeenAt",
        ["SourceType"]       = "o.SourceType",
        ["OriginalSource"]   = "o.OriginalSource",
        ["CreatedAt"]        = "o.FirstSyncedAt"
    };

    // -------- Identities (Identities table -- separate from Objects) --------
    // Email -> PrimaryEmail, ManagerId -> ManagerIdentityId per the V004/V012 schema.
    private static readonly Dictionary<string, string> IdentitiesSelectMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FirstName"]    = "i.FirstName",
        ["LastName"]     = "i.LastName",
        ["Email"]        = "i.PrimaryEmail",
        ["Department"]   = "i.Department",
        ["JobTitle"]     = "i.JobTitle",
        ["ManagerId"]    = "CONVERT(nvarchar(40), i.ManagerIdentityId)",
        ["Status"]       = "i.Status",
        ["CreatedAt"]    = "FORMAT(i.CreatedAt, 'yyyy-MM-dd HH:mm')"
    };
    private static readonly Dictionary<string, string> IdentitiesFilterMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FirstName"]    = "i.FirstName",
        ["LastName"]     = "i.LastName",
        ["Email"]        = "i.PrimaryEmail",
        ["Department"]   = "i.Department",
        ["JobTitle"]     = "i.JobTitle",
        ["ManagerId"]    = "i.ManagerIdentityId",
        ["Status"]       = "i.Status",
        ["CreatedAt"]    = "i.CreatedAt"
    };

    // -------- Licenses (LicenseAssignments INNER JOIN LicensePools LEFT JOIN Objects) --------
    // User-facing names AssignedDate / LastActiveDate map to the V056 columns
    // AssignedAt / LastUsedAt. InactiveDays is a computed expression.
    private const string InactiveDaysExpr =
        "DATEDIFF(day, COALESCE(la.LastUsedAt, la.AssignedAt), SYSUTCDATETIME())";
    private static readonly Dictionary<string, string> LicensesSelectMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"]         = "o.DisplayName",
        ["Email"]               = "o.Email",
        ["PoolName"]            = "lp.SkuName",
        ["SkuName"]             = "lp.SkuName",
        ["AssignedDate"]        = "FORMAT(la.AssignedAt, 'yyyy-MM-dd HH:mm')",
        ["LastActiveDate"]      = "FORMAT(la.LastUsedAt, 'yyyy-MM-dd HH:mm')",
        ["CostPerUnitMonthly"]  = "lp.CostPerUnitMonthly",
        ["InactiveDays"]        = InactiveDaysExpr
    };
    private static readonly Dictionary<string, string> LicensesFilterMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"]         = "o.DisplayName",
        ["Email"]               = "o.Email",
        ["PoolName"]            = "lp.SkuName",
        ["SkuName"]             = "lp.SkuName",
        ["AssignedDate"]        = "la.AssignedAt",
        ["LastActiveDate"]      = "la.LastUsedAt",
        ["CostPerUnitMonthly"]  = "lp.CostPerUnitMonthly",
        ["InactiveDays"]        = InactiveDaysExpr
    };

    // -------- Legacy "Objects" union -- preserves backward compatibility --------
    // Seeded reports (e.g. V107 Non-Expiring Passwords) use DataSource="Objects" with
    // ObjectClassFilter="user" and reference the broader column surface. Keep the
    // historical superset so those reports never break.
    private static readonly Dictionary<string, string> ObjectsSelectMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"] = "o.DisplayName",
        ["Username"] = "o.Username",
        ["UserPrincipalName"] = "o.UserPrincipalName",
        ["ObjectClass"] = "o.ObjectClass",
        ["IsActive"] = "CASE WHEN o.IsActive = 1 THEN 'Yes' ELSE 'No' END",
        ["FirstSyncedAt"] = "FORMAT(o.FirstSyncedAt, 'yyyy-MM-dd HH:mm')",
        ["LastSyncedAt"] = "FORMAT(o.LastSyncedAt, 'yyyy-MM-dd HH:mm')",
        ["LastSeenAt"] = "FORMAT(o.LastSeenAt, 'yyyy-MM-dd HH:mm')",
        ["SourceType"] = "o.SourceType",
        ["OriginalSource"] = "o.OriginalSource",
        ["DN"] = "o.DN",
        ["CN"] = "o.CN",
        ["Description"] = "o.Description",
        ["Email"] = "o.Email",
        ["FirstName"] = "o.FirstName",
        ["LastName"] = "o.LastName",
        ["Department"] = "o.Department",
        ["JobTitle"] = "o.JobTitle",
        ["Phone"] = "o.Phone",
        ["MobilePhone"] = "o.MobilePhone",
        ["Company"] = "o.Company",
        ["Division"] = "o.Division",
        ["Office"] = "o.Office",
        ["City"] = "o.City",
        ["State"] = "o.State",
        ["Country"] = "o.Country",
        ["PostalCode"] = "o.PostalCode",
        ["StreetAddress"] = "o.StreetAddress",
        ["EmployeeId"] = "o.EmployeeId",
        ["EmployeeType"] = "o.EmployeeType",
        ["PasswordLastSet"] = "FORMAT(o.PasswordLastSet, 'yyyy-MM-dd HH:mm')",
        ["PasswordNeverExpires"] = "CASE WHEN o.PasswordNeverExpires = 1 THEN 'Yes' ELSE 'No' END",
        ["IsBuiltIn"] = "CASE WHEN o.IsBuiltIn = 1 THEN 'Yes' ELSE 'No' END",
        ["IsAdminSDHolder"] = "CASE WHEN o.IsAdminSDHolder = 1 THEN 'Yes' ELSE 'No' END",
        ["UserAccountControl"] = "o.UserAccountControl",
        ["ConnectionName"] = "dc.Name",
        ["ManagerDisplayName"] = "mgr.DisplayName"
    };
    private static readonly Dictionary<string, string> ObjectsFilterMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayName"] = "o.DisplayName",
        ["Username"] = "o.Username",
        ["UserPrincipalName"] = "o.UserPrincipalName",
        ["ObjectClass"] = "o.ObjectClass",
        ["IsActive"] = "o.IsActive",
        ["FirstSyncedAt"] = "o.FirstSyncedAt",
        ["LastSyncedAt"] = "o.LastSyncedAt",
        ["LastSeenAt"] = "o.LastSeenAt",
        ["SourceType"] = "o.SourceType",
        ["OriginalSource"] = "o.OriginalSource",
        ["DN"] = "o.DN",
        ["CN"] = "o.CN",
        ["Description"] = "o.Description",
        ["Email"] = "o.Email",
        ["FirstName"] = "o.FirstName",
        ["LastName"] = "o.LastName",
        ["Department"] = "o.Department",
        ["JobTitle"] = "o.JobTitle",
        ["Phone"] = "o.Phone",
        ["MobilePhone"] = "o.MobilePhone",
        ["Company"] = "o.Company",
        ["Division"] = "o.Division",
        ["Office"] = "o.Office",
        ["City"] = "o.City",
        ["State"] = "o.State",
        ["Country"] = "o.Country",
        ["PostalCode"] = "o.PostalCode",
        ["StreetAddress"] = "o.StreetAddress",
        ["EmployeeId"] = "o.EmployeeId",
        ["EmployeeType"] = "o.EmployeeType",
        ["PasswordLastSet"] = "o.PasswordLastSet",
        ["PasswordNeverExpires"] = "o.PasswordNeverExpires",
        ["IsBuiltIn"] = "o.IsBuiltIn",
        ["IsAdminSDHolder"] = "o.IsAdminSDHolder",
        ["UserAccountControl"] = "o.UserAccountControl",
        ["ConnectionName"] = "dc.Name",
        ["ManagerDisplayName"] = "mgr.DisplayName"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> SelectMaps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Objects"]    = ObjectsSelectMap,
        ["Users"]      = UsersSelectMap,
        ["Groups"]     = GroupsSelectMap,
        ["Computers"]  = ComputersSelectMap,
        ["Identities"] = IdentitiesSelectMap,
        ["Licenses"]   = LicensesSelectMap
    };

    private static readonly Dictionary<string, Dictionary<string, string>> FilterMaps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Objects"]    = ObjectsFilterMap,
        ["Users"]      = UsersFilterMap,
        ["Groups"]     = GroupsFilterMap,
        ["Computers"]  = ComputersFilterMap,
        ["Identities"] = IdentitiesFilterMap,
        ["Licenses"]   = LicensesFilterMap
    };

    private static readonly List<string> CommonAttributes = new()
    {
        "mail", "telephoneNumber", "department", "title", "manager",
        "company", "l", "c", "employeeID", "operatingSystem",
        "operatingSystemVersion", "dNSHostName", "servicePrincipalName",
        "memberOf", "whenCreated", "whenChanged", "lastLogonTimestamp",
        "lastLogon", "pwdLastSet", "accountExpires", "description",
        "managedBy", "info", "physicalDeliveryOfficeName",
        "streetAddress", "st", "postalCode", "targetAddress",
        "proxyAddresses", "userPrincipalName"
    };

    public ReportExecutionEngine(IConfiguration configuration, ILogger<ReportExecutionEngine> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException("Connection string not found");
        _logger = logger;
    }

    public Dictionary<string, string> GetCoreFieldMap() => new(ObjectsSelectMap);

    public Dictionary<string, string> GetCoreFieldMap(string dataSource)
    {
        if (SelectMaps.TryGetValue(dataSource ?? "Objects", out var map))
            return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, string>(ObjectsSelectMap, StringComparer.OrdinalIgnoreCase);
    }

    public List<string> GetCommonAttributes() => new(CommonAttributes);

    /// <summary>
    /// Validates a VisualReportDefinition against the per-DataSource allow-list.
    /// Throws InvalidOperationException for any non-allow-listed column / filter / sort.
    /// Exposed so tests (and any thin REST endpoint) can fail fast before SQL is built.
    /// </summary>
    internal static void ValidateAllowList(VisualReportDefinition def)
    {
        var ds = def.DataSource ?? "Objects";
        if (!SelectMaps.TryGetValue(ds, out var selectMap) ||
            !FilterMaps.TryGetValue(ds, out var filterMap))
        {
            throw new InvalidOperationException(
                string.Concat("DataSource '", ds, "' is not supported."));
        }

        foreach (var col in def.Columns)
        {
            if (col.IsAttribute)
            {
                if (!SafeFieldNameRegex.IsMatch(col.Field))
                    throw new InvalidOperationException(
                        string.Concat("Attribute name '", col.Field, "' contains disallowed characters."));
                ValidateNoInjection(col.Field);
            }
            else if (!selectMap.ContainsKey(col.Field))
            {
                throw new InvalidOperationException(
                    string.Concat("Column '", col.Field, "' is not in the allow-list for DataSource=", ds, "."));
            }
        }

        foreach (var filter in def.Filters)
        {
            if (filter.IsAttribute)
            {
                if (!SafeFieldNameRegex.IsMatch(filter.Field))
                    throw new InvalidOperationException(
                        string.Concat("Filter attribute '", filter.Field, "' contains disallowed characters."));
                ValidateNoInjection(filter.Field);
            }
            else if (!filterMap.ContainsKey(filter.Field))
            {
                throw new InvalidOperationException(
                    string.Concat("Filter column '", filter.Field, "' is not in the allow-list for DataSource=", ds, "."));
            }

            if (filter.Value != null) ValidateNoInjection(filter.Value);
        }

        foreach (var sort in def.SortBy)
        {
            if (sort.IsAttribute) continue;
            if (!filterMap.ContainsKey(sort.Field))
                throw new InvalidOperationException(
                    string.Concat("Sort column '", sort.Field, "' is not in the allow-list for DataSource=", ds, "."));
        }
    }

    // Back-compat shim for tests that still call the Objects-specific name.
    internal static void ValidateAllowListForObjects(VisualReportDefinition def) => ValidateAllowList(def);

    public async Task<ReportResult> ExecuteAsync(VisualReportDefinition def, int? previewLimit = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new ReportResult();

        try
        {
            result = (def.DataSource ?? "Objects") switch
            {
                "Objects"    => await ExecuteObjectsQueryAsync(def, "Objects", null, previewLimit, ct),
                "Users"      => await ExecuteObjectsQueryAsync(def, "Users", "user", previewLimit, ct),
                "Groups"     => await ExecuteObjectsQueryAsync(def, "Groups", "group", previewLimit, ct),
                "Computers"  => await ExecuteObjectsQueryAsync(def, "Computers", "computer", previewLimit, ct),
                "Identities" => await ExecuteIdentitiesQueryAsync(def, previewLimit, ct),
                "Licenses"   => await ExecuteLicensesQueryAsync(def, previewLimit, ct),
                _            => await ExecuteObjectsQueryAsync(def, "Objects", null, previewLimit, ct)
            };
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.LogError(ex, "Report execution failed for DataSource={DataSource}", def.DataSource);
        }

        result.DurationMs = (int)sw.ElapsedMilliseconds;
        return result;
    }

    private async Task<ReportResult> ExecuteObjectsQueryAsync(
        VisualReportDefinition def,
        string sourceKey,
        string? autoObjectClass,
        int? previewLimit,
        CancellationToken ct)
    {
        var selectMap = SelectMaps[sourceKey];
        var filterMap = FilterMaps[sourceKey];

        var selectParts = new List<string>();
        var attributeJoins = new List<string>();
        var attrIndex = 0;
        var parameters = new DynamicParameters();
        var needsManagerJoin = false;

        // Build SELECT list
        foreach (var col in def.Columns.OrderBy(c => c.Order))
        {
            if (!col.IsAttribute)
            {
                if (!selectMap.TryGetValue(col.Field, out var expr))
                {
                    throw new InvalidOperationException(
                        string.Concat("Column '", col.Field, "' is not in the allow-list for DataSource=", sourceKey, "."));
                }
                selectParts.Add(string.Concat(expr, " AS [", SanitizeLabel(col.Label), "]"));
                if (RequiresManagerJoin(col.Field, sourceKey))
                    needsManagerJoin = true;
            }
            else
            {
                if (!SafeFieldNameRegex.IsMatch(col.Field))
                {
                    throw new InvalidOperationException(
                        string.Concat("Attribute name '", col.Field, "' contains disallowed characters. Allowed: A-Z, a-z, 0-9, hyphen, underscore."));
                }
                ValidateNoInjection(col.Field);
                var alias = string.Concat("a", attrIndex.ToString());
                attributeJoins.Add(
                    string.Concat("LEFT JOIN ObjectAttributes ", alias, " ON ", alias, ".ObjectId = o.Id AND ", alias, ".AttributeName = '", col.Field, "'"));
                selectParts.Add(string.Concat(alias, ".AttributeValue AS [", SanitizeLabel(col.Label), "]"));
                attrIndex++;
            }
        }

        if (selectParts.Count == 0) selectParts.Add("o.DisplayName AS [Display Name]");

        // Build WHERE
        var whereParts = new List<string> { "o.DeletedAt IS NULL" };
        if (!def.IncludeInactive) whereParts.Add("o.IsActive = 1");

        // Per-source automatic ObjectClass restriction (Users/Groups/Computers).
        // Legacy "Objects" still honors def.ObjectClassFilter for backward compat.
        var classFilter = autoObjectClass ?? def.ObjectClassFilter;
        if (!string.IsNullOrEmpty(classFilter))
        {
            whereParts.Add("o.ObjectClass = @objectClass");
            parameters.Add("objectClass", classFilter);
        }

        // Apply filters
        int filterIndex = 0;
        foreach (var filter in def.Filters)
        {
            string paramName = string.Concat("f", filterIndex.ToString());

            if (!filter.IsAttribute)
            {
                if (!filterMap.TryGetValue(filter.Field, out var filterCol))
                {
                    throw new InvalidOperationException(
                        string.Concat("Filter column '", filter.Field, "' is not in the allow-list for DataSource=", sourceKey, "."));
                }
                var filterExpr = BuildFilterExpression(filterCol, filter, paramName, parameters);
                if (filterExpr != null)
                {
                    whereParts.Add(filterExpr);
                    filterIndex++;
                    if (RequiresManagerJoin(filter.Field, sourceKey))
                        needsManagerJoin = true;
                }
            }
            else
            {
                if (!SafeFieldNameRegex.IsMatch(filter.Field))
                {
                    throw new InvalidOperationException(
                        string.Concat("Filter attribute '", filter.Field, "' contains disallowed characters."));
                }
                ValidateNoInjection(filter.Field);
                var alias = string.Concat("af", filterIndex.ToString());
                attributeJoins.Add(
                    string.Concat("LEFT JOIN ObjectAttributes ", alias, " ON ", alias, ".ObjectId = o.Id AND ", alias, ".AttributeName = '", filter.Field, "'"));
                var filterExpr = BuildFilterExpression(string.Concat(alias, ".AttributeValue"), filter, paramName, parameters);
                if (filterExpr != null)
                {
                    whereParts.Add(filterExpr);
                    filterIndex++;
                }
            }
        }

        // Build ORDER BY
        var orderParts = new List<string>();
        foreach (var sort in def.SortBy)
        {
            var dir = sort.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            if (sort.IsAttribute)
            {
                continue;
            }
            if (!filterMap.TryGetValue(sort.Field, out var sortExpr))
            {
                throw new InvalidOperationException(
                    string.Concat("Sort column '", sort.Field, "' is not in the allow-list for DataSource=", sourceKey, "."));
            }
            orderParts.Add(string.Concat(sortExpr, " ", dir));
            if (RequiresManagerJoin(sort.Field, sourceKey))
                needsManagerJoin = true;
        }
        if (orderParts.Count == 0) orderParts.Add("o.DisplayName ASC");

        int effectiveLimit = previewLimit ?? def.MaxRows ?? 10000;
        string topClause = string.Concat("TOP ", effectiveLimit.ToString());

        string managerJoin = needsManagerJoin
            ? "LEFT JOIN Objects mgr ON mgr.Id = o.ManagerObjectId"
            : "";

        string sql = string.Concat(
            "SELECT ", topClause, "\n    ",
            string.Join(",\n    ", selectParts),
            "\nFROM Objects o",
            "\nLEFT JOIN DirectoryConnections dc ON dc.Id = o.SourceConnectionId",
            needsManagerJoin ? string.Concat("\n", managerJoin) : "",
            attributeJoins.Count > 0 ? string.Concat("\n", string.Join("\n", attributeJoins)) : "",
            "\nWHERE ", string.Join("\n  AND ", whereParts),
            "\nORDER BY ", string.Join(", ", orderParts));

        _logger.LogDebug("Report engine SQL: {Sql}", sql);

        using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<dynamic>(sql, parameters, commandTimeout: 120)).ToList();

        var headers = def.Columns.OrderBy(c => c.Order).Select(c => c.Label).ToList();

        return new ReportResult
        {
            Headers = headers,
            Rows = rows.Select(r =>
            {
                var dict = (IDictionary<string, object>)r;
                return dict.Values.Select(v => v?.ToString()).ToList();
            }).ToList(),
            TotalRows = rows.Count,
            IsTruncated = rows.Count >= effectiveLimit
        };
    }

    private static bool RequiresManagerJoin(string field, string sourceKey)
    {
        // Manager join is needed for the legacy ManagerDisplayName column (Objects) and for
        // the per-source "Manager" column (Users) -- both resolve via mgr.* in their maps.
        return field.Equals("ManagerDisplayName", StringComparison.OrdinalIgnoreCase)
            || (sourceKey.Equals("Users", StringComparison.OrdinalIgnoreCase) && field.Equals("Manager", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ReportResult> ExecuteIdentitiesQueryAsync(VisualReportDefinition def, int? previewLimit, CancellationToken ct)
    {
        var selectMap = IdentitiesSelectMap;
        var filterMap = IdentitiesFilterMap;

        var selectParts = new List<string>();
        var parameters = new DynamicParameters();

        foreach (var col in def.Columns.OrderBy(c => c.Order))
        {
            if (col.IsAttribute)
            {
                // Identities table has no key/value attributes table -- reject explicitly so the
                // user sees a controlled error instead of a SQL exception.
                throw new InvalidOperationException(
                    "Attribute columns are not supported for DataSource=Identities.");
            }
            if (!selectMap.TryGetValue(col.Field, out var expr))
            {
                throw new InvalidOperationException(
                    string.Concat("Column '", col.Field, "' is not in the allow-list for DataSource=Identities."));
            }
            selectParts.Add(string.Concat(expr, " AS [", SanitizeLabel(col.Label), "]"));
        }

        if (selectParts.Count == 0) selectParts.Add("i.DisplayName AS [Display Name]");

        var whereParts = new List<string>();
        if (!def.IncludeInactive) whereParts.Add("i.IsActive = 1");

        int filterIndex = 0;
        foreach (var filter in def.Filters)
        {
            if (filter.IsAttribute)
                throw new InvalidOperationException(
                    "Attribute filters are not supported for DataSource=Identities.");

            string paramName = string.Concat("f", filterIndex.ToString());
            if (!filterMap.TryGetValue(filter.Field, out var filterCol))
            {
                throw new InvalidOperationException(
                    string.Concat("Filter column '", filter.Field, "' is not in the allow-list for DataSource=Identities."));
            }
            var filterExpr = BuildFilterExpression(filterCol, filter, paramName, parameters);
            if (filterExpr != null)
            {
                whereParts.Add(filterExpr);
                filterIndex++;
            }
        }

        var orderParts = new List<string>();
        foreach (var sort in def.SortBy)
        {
            if (sort.IsAttribute) continue;
            var dir = sort.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            if (!filterMap.TryGetValue(sort.Field, out var sortExpr))
            {
                throw new InvalidOperationException(
                    string.Concat("Sort column '", sort.Field, "' is not in the allow-list for DataSource=Identities."));
            }
            orderParts.Add(string.Concat(sortExpr, " ", dir));
        }
        if (orderParts.Count == 0) orderParts.Add("i.DisplayName ASC");

        int effectiveLimit = previewLimit ?? def.MaxRows ?? 10000;
        string topClause = string.Concat("TOP ", effectiveLimit.ToString());

        string sql = string.Concat(
            "SELECT ", topClause, "\n    ",
            string.Join(",\n    ", selectParts),
            "\nFROM Identities i",
            whereParts.Count > 0 ? string.Concat("\nWHERE ", string.Join("\n  AND ", whereParts)) : "",
            "\nORDER BY ", string.Join(", ", orderParts));

        _logger.LogDebug("Report engine SQL: {Sql}", sql);

        using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<dynamic>(sql, parameters, commandTimeout: 120)).ToList();

        var headers = def.Columns.OrderBy(c => c.Order).Select(c => c.Label).ToList();

        return new ReportResult
        {
            Headers = headers,
            Rows = rows.Select(r =>
            {
                var dict = (IDictionary<string, object>)r;
                return dict.Values.Select(v => v?.ToString()).ToList();
            }).ToList(),
            TotalRows = rows.Count,
            IsTruncated = rows.Count >= effectiveLimit
        };
    }

    private async Task<ReportResult> ExecuteLicensesQueryAsync(VisualReportDefinition def, int? previewLimit, CancellationToken ct)
    {
        var built = BuildLicensesSql(def, previewLimit);

        using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<dynamic>(built.Sql, built.Parameters, commandTimeout: 120)).ToList();

        var headers = def.Columns.Count > 0
            ? def.Columns.OrderBy(c => c.Order).Select(c => c.Label).ToList()
            : DefaultLicenseHeaders();

        if (rows.Count == 0)
        {
            return new ReportResult
            {
                Headers = headers,
                Rows = new List<List<string?>>(),
                TotalRows = 0
            };
        }

        return new ReportResult
        {
            Headers = headers,
            Rows = rows.Select(r =>
            {
                var dict = (IDictionary<string, object>)r;
                return dict.Values.Select(v => v?.ToString()).ToList();
            }).ToList(),
            TotalRows = rows.Count,
            IsTruncated = rows.Count >= built.EffectiveLimit
        };
    }

    private static List<string> DefaultLicenseHeaders() => new()
    {
        "License SKU", "Part Number", "User", "Department", "Assigned Date", "User Status", "Monthly Cost"
    };

    internal readonly struct LicensesQuery
    {
        public string Sql { get; }
        public DynamicParameters Parameters { get; }
        public int EffectiveLimit { get; }
        public LicensesQuery(string sql, DynamicParameters p, int limit) { Sql = sql; Parameters = p; EffectiveLimit = limit; }
    }

    /// <summary>
    /// Builds the SQL + parameters for the visual-builder Licenses path. Internal so the
    /// test project can regression-check the SQL shape without a live SQL Server.
    /// </summary>
    internal static LicensesQuery BuildLicensesSql(VisualReportDefinition def, int? previewLimit)
    {
        var selectMap = LicensesSelectMap;
        var filterMap = LicensesFilterMap;

        var parameters = new DynamicParameters();
        var selectParts = new List<string>();

        foreach (var col in def.Columns.OrderBy(c => c.Order))
        {
            if (col.IsAttribute)
            {
                throw new InvalidOperationException(
                    "Attribute columns are not supported for DataSource=Licenses.");
            }
            if (!selectMap.TryGetValue(col.Field, out var expr))
            {
                throw new InvalidOperationException(
                    string.Concat("Column '", col.Field, "' is not in the allow-list for DataSource=Licenses."));
            }
            selectParts.Add(string.Concat(expr, " AS [", SanitizeLabel(col.Label), "]"));
        }

        // Default SELECT preserves the historical License Waste shape so existing visual-builder
        // reports with no explicit columns still surface useful data.
        if (selectParts.Count == 0)
        {
            selectParts.AddRange(new[]
            {
                "lp.SkuName AS [License SKU]",
                "lp.SkuPartNumber AS [Part Number]",
                "o.DisplayName AS [User]",
                "o.Department AS [Department]",
                "FORMAT(la.AssignedAt, 'yyyy-MM-dd HH:mm') AS [Assigned Date]",
                "CASE WHEN o.IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS [User Status]",
                "lp.CostPerUnitMonthly AS [Monthly Cost]"
            });
        }

        var whereParts = new List<string> { "la.IsActive = 1" };

        int filterIndex = 0;
        foreach (var filter in def.Filters)
        {
            if (filter.IsAttribute)
                throw new InvalidOperationException(
                    "Attribute filters are not supported for DataSource=Licenses.");

            string paramName = string.Concat("f", filterIndex.ToString());
            if (!filterMap.TryGetValue(filter.Field, out var filterCol))
            {
                throw new InvalidOperationException(
                    string.Concat("Filter column '", filter.Field, "' is not in the allow-list for DataSource=Licenses."));
            }
            var filterExpr = BuildFilterExpression(filterCol, filter, paramName, parameters);
            if (filterExpr != null)
            {
                whereParts.Add(filterExpr);
                filterIndex++;
            }
        }

        var orderParts = new List<string>();
        foreach (var sort in def.SortBy)
        {
            if (sort.IsAttribute) continue;
            var dir = sort.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            if (!filterMap.TryGetValue(sort.Field, out var sortExpr))
            {
                throw new InvalidOperationException(
                    string.Concat("Sort column '", sort.Field, "' is not in the allow-list for DataSource=Licenses."));
            }
            orderParts.Add(string.Concat(sortExpr, " ", dir));
        }
        if (orderParts.Count == 0) orderParts.Add("lp.SkuName ASC, o.DisplayName ASC");

        int effectiveLimit = previewLimit ?? def.MaxRows ?? 10000;

        string sql = string.Concat(
            "SELECT TOP ", effectiveLimit.ToString(), "\n    ",
            string.Join(",\n    ", selectParts),
            "\nFROM LicenseAssignments la",
            "\nINNER JOIN LicensePools lp ON lp.Id = la.LicensePoolId",
            "\nLEFT JOIN Objects o ON o.Id = la.ObjectId",
            "\nWHERE ", string.Join("\n  AND ", whereParts),
            "\nORDER BY ", string.Join(", ", orderParts));

        return new LicensesQuery(sql, parameters, effectiveLimit);
    }

    internal static string? BuildFilterExpression(string fieldExpr, VisualReportFilter filter, string paramName, DynamicParameters p)
    {
        // Validate the filter value doesn't contain injection attempts
        if (filter.Value != null)
            ValidateNoInjection(filter.Value);

        return filter.Operator switch
        {
            "equals" => AddParamAndReturn(p, paramName, filter.Value, string.Concat(fieldExpr, " = @", paramName)),
            "not_equals" => AddParamAndReturn(p, paramName, filter.Value, string.Concat(fieldExpr, " != @", paramName)),
            "contains" => AddParamAndReturn(p, paramName, string.Concat("%", filter.Value, "%"), string.Concat(fieldExpr, " LIKE @", paramName)),
            "startswith" => AddParamAndReturn(p, paramName, string.Concat(filter.Value, "%"), string.Concat(fieldExpr, " LIKE @", paramName)),
            "is_empty" => string.Concat("(", fieldExpr, " IS NULL OR ", fieldExpr, " = '')"),
            "is_not_empty" => string.Concat("(", fieldExpr, " IS NOT NULL AND ", fieldExpr, " != '')"),
            "greater_than" => AddParamAndReturn(p, paramName, filter.Value, string.Concat(fieldExpr, " > @", paramName)),
            "less_than" => AddParamAndReturn(p, paramName, filter.Value, string.Concat(fieldExpr, " < @", paramName)),
            "in_last_n_days" => BuildInLastNDays(fieldExpr, filter.Value, paramName, p),
            _ => null
        };
    }

    private static string? BuildInLastNDays(string fieldExpr, string? value, string paramName, DynamicParameters p)
    {
        // Reject if value is not a positive integer -- ensures parameterized DATEADD with no chance of injection.
        if (!int.TryParse(value, out var days) || days <= 0)
        {
            return null;
        }
        p.Add(paramName, days);
        return string.Concat(fieldExpr, " >= DATEADD(day, -@", paramName, ", SYSUTCDATETIME())");
    }

    private static string AddParamAndReturn(DynamicParameters p, string paramName, string? value, string expression)
    {
        p.Add(paramName, value);
        return expression;
    }

    private static string SanitizeLabel(string label)
    {
        // Remove brackets and other SQL-dangerous chars from labels
        return label.Replace("[", "").Replace("]", "").Replace("'", "").Replace(";", "");
    }

    private static void ValidateNoInjection(string value)
    {
        foreach (var pattern in DangerousPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Concat("Value contains disallowed keyword: ", pattern));
            }
        }
    }
}
