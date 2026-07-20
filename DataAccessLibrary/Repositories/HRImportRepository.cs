using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Services;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DataAccessLibrary.Repositories;

public class HRImportRepository : DapperRepositoryBase, IHRImportRepository
{
    private readonly IAuditLogService _auditLogService;

    public HRImportRepository(IConfiguration configuration, IGlobalLogger logger, IAuditLogService auditLogService)
        : base(configuration, logger)
    {
        _auditLogService = auditLogService;
    }

    // ========== Field Mapping CRUD ==========

    public Task<List<HRFieldMapping>> GetFieldMappingsAsync(Guid directoryConnectionId, CancellationToken ct = default)
    {
        return ExecuteAsync(async conn =>
        {
            var results = await conn.QueryAsync<HRFieldMapping>(
                @"SELECT Id, DirectoryConnectionId, SourceField, TargetField, IsRequired,
                         DefaultValue, Transformation, MappingOrder, IsEnabled, IsKeyField
                  FROM HRFieldMappings
                  WHERE DirectoryConnectionId = @ConnectionId AND IsEnabled = 1
                  ORDER BY MappingOrder",
                new { ConnectionId = directoryConnectionId });
            return results.ToList();
        }, ct);
    }

    public Task BulkCreateFieldMappingsAsync(List<HRFieldMapping> mappings, CancellationToken ct = default)
    {
        return ExecuteNonQueryAsync(async conn =>
        {
            await conn.ExecuteAsync(
                @"INSERT INTO HRFieldMappings (Id, DirectoryConnectionId, SourceField, TargetField,
                                               IsRequired, DefaultValue, Transformation, MappingOrder, IsEnabled, IsKeyField)
                  VALUES (@Id, @DirectoryConnectionId, @SourceField, @TargetField,
                          @IsRequired, @DefaultValue, @Transformation, @MappingOrder, @IsEnabled, @IsKeyField)",
                mappings);
        }, ct);
    }

    public Task DeleteAllFieldMappingsAsync(Guid directoryConnectionId, CancellationToken ct = default)
    {
        return ExecuteNonQueryAsync(async conn =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM HRFieldMappings WHERE DirectoryConnectionId = @ConnectionId",
                new { ConnectionId = directoryConnectionId });
        }, ct);
    }

    // ========== Import Run Tracking ==========

    public Task<Guid> CreateImportRunAsync(HRImportRun run, CancellationToken ct = default)
    {
        return ExecuteAsync(async conn =>
        {
            await conn.ExecuteAsync(
                @"INSERT INTO HRImportRuns (Id, SyncProjectId, Status, SourceFileName,
                                            TotalRecords, CreatedRecords, UpdatedRecords, SkippedRecords,
                                            ErrorRecords, EnabledRecords, DisabledRecords,
                                            ErrorDetails, StartedAt, CompletedAt, DurationSeconds)
                  VALUES (@Id, @SyncProjectId, @Status, @SourceFileName,
                          @TotalRecords, @CreatedRecords, @UpdatedRecords, @SkippedRecords,
                          @ErrorRecords, @EnabledRecords, @DisabledRecords,
                          @ErrorDetails, @StartedAt, @CompletedAt, @DurationSeconds)",
                run);
            return run.Id;
        }, ct);
    }

    public Task UpdateImportRunAsync(HRImportRun run, CancellationToken ct = default)
    {
        return ExecuteNonQueryAsync(async conn =>
        {
            await conn.ExecuteAsync(
                @"UPDATE HRImportRuns SET
                    Status = @Status, TotalRecords = @TotalRecords,
                    CreatedRecords = @CreatedRecords, UpdatedRecords = @UpdatedRecords,
                    SkippedRecords = @SkippedRecords, ErrorRecords = @ErrorRecords,
                    EnabledRecords = @EnabledRecords, DisabledRecords = @DisabledRecords,
                    ErrorDetails = @ErrorDetails, CompletedAt = @CompletedAt, DurationSeconds = @DurationSeconds
                  WHERE Id = @Id",
                run);
        }, ct);
    }

    public Task<List<HRImportRun>> GetImportRunsAsync(Guid syncProjectId, int top = 50, CancellationToken ct = default)
    {
        return ExecuteAsync(async conn =>
        {
            var results = await conn.QueryAsync<HRImportRun>(
                @"SELECT TOP (@Top) * FROM HRImportRuns
                  WHERE SyncProjectId = @SyncProjectId
                  ORDER BY StartedAt DESC",
                new { SyncProjectId = syncProjectId, Top = top });
            return results.ToList();
        }, ct);
    }

    public Task<HRImportRun?> GetLatestImportRunAsync(Guid syncProjectId, CancellationToken ct = default)
    {
        return ExecuteAsync(async conn =>
        {
            return await conn.QueryFirstOrDefaultAsync<HRImportRun>(
                @"SELECT TOP 1 * FROM HRImportRuns
                  WHERE SyncProjectId = @SyncProjectId
                  ORDER BY StartedAt DESC",
                new { SyncProjectId = syncProjectId });
        }, ct);
    }

    // ========== Core Import: Bulk Upsert Identities ==========

    public Task<HRImportResult> BulkUpsertIdentitiesAsync(
        List<Dictionary<string, object?>> records,
        List<HRFieldMapping> mappings,
        string uniqueIdField,
        Guid connectionId,
        CancellationToken ct = default,
        HRImportStepConfig? stepConfig = null)
    {
        return ExecuteAsync(async conn =>
        {
            var result = new HRImportResult();
            var auditEntries = new List<ChangeAuditEntry>();

            // Build the target field → source field map
            var fieldMap = mappings
                .Where(m => m.IsEnabled)
                .OrderBy(m => m.MappingOrder)
                .ToDictionary(m => m.TargetField, m => m);

            // Find the key mapping (which source field maps to the unique ID)
            var keyMapping = mappings.FirstOrDefault(m => m.IsKeyField && m.IsEnabled)
                ?? mappings.FirstOrDefault(m => m.TargetField.Equals(uniqueIdField, StringComparison.OrdinalIgnoreCase) && m.IsEnabled);

            if (keyMapping == null)
            {
                result.ErrorMessage = $"No key field mapping found for unique ID field '{uniqueIdField}'";
                result.Errors = records.Count;
                return result;
            }

            _logger.LogInformation("HR Import: Key field resolved — UniqueIdField='{UniqueIdField}', " +
                "SourceField='{SourceField}', TargetField='{TargetField}', " +
                "IsKeyField={IsKeyField}, Records={Records}",
                uniqueIdField, keyMapping.SourceField, keyMapping.TargetField,
                keyMapping.IsKeyField, records.Count);

            // Valid Identity columns we can write to
            var validColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Biographic & personal
                "DisplayName", "FirstName", "LastName", "MiddleName", "Suffix", "Salutation",
                "PreferredName", "DateOfBirth", "Gender", "NationalId", "PhotoUrl",
                // Contact
                "PrimaryEmail", "SecondaryEmail", "PrimaryPhone", "MobilePhone", "HomePhone", "Fax",
                "StreetAddress", "City", "State", "PostalCode", "Country",
                // Organizational & job
                "EmployeeId", "JobTitle", "Department", "Division", "Company", "Office", "Building",
                "Floor", "Room", "CostCenter", "ProfitCenter", "IdentityType", "EmployeeType",
                "ContractType", "JobCode", "JobFamily", "PayGrade", "Organization", "BusinessUnit",
                "LegalEntity", "Region", "Site", "WorkSchedule",
                // Dates
                "HireDate", "TerminationDate", "LastWorkDay", "StartDate", "EndDate",
                // Description & notes
                "Description", "Notes",
                // Manager & sponsor
                "ManagerEmployeeId", "ManagerDisplayName", "Sponsor", "SponsorEmail",
                // Contractor / vendor
                "VendorName", "PONumber",
                // Physical access
                "BadgeNumber",
                // Technical & security
                "Username", "UserPrincipalName", "Status", "SecurityClearance",
                // Localization
                "PreferredLanguage", "TimeZone", "Locale",
                // Custom attributes (1-20)
                "CustomAttribute1", "CustomAttribute2", "CustomAttribute3", "CustomAttribute4",
                "CustomAttribute5", "CustomAttribute6", "CustomAttribute7", "CustomAttribute8",
                "CustomAttribute9", "CustomAttribute10", "CustomAttribute11", "CustomAttribute12",
                "CustomAttribute13", "CustomAttribute14", "CustomAttribute15", "CustomAttribute16",
                "CustomAttribute17", "CustomAttribute18", "CustomAttribute19", "CustomAttribute20"
            };

            // Lifecycle fields used for Joiner/Mover/Leaver event detection
            var lifecycleFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Department", "JobTitle", "Status", "Office", "Company", "TerminationDate", "StartDate", "EndDate" };

            // Normalize uniqueIdField: "Employee ID" → "EmployeeId", "first_name" → "FirstName"
            uniqueIdField = NormalizeToColumnName(uniqueIdField, validColumns);

            // The unique-ID field is interpolated as a SQL identifier below. Reject anything that is
            // not an allow-listed Identity column instead of executing an attacker-controlled identifier.
            if (!validColumns.Contains(uniqueIdField))
            {
                result.ErrorMessage = $"Unique ID field '{uniqueIdField}' is not a recognized Identity column and was rejected.";
                result.Errors = records.Count;
                return result;
            }

            for (int rowIdx = 0; rowIdx < records.Count; rowIdx++)
            {
                var record = records[rowIdx];

                try
                {
                    // Extract the unique key value from this record
                    var keyValue = GetSourceValue(record, keyMapping.SourceField);
                    if (string.IsNullOrWhiteSpace(keyValue))
                    {
                        result.Skipped++;
                        result.ErrorList.Add(new HRImportError
                        {
                            Row = rowIdx + 1,
                            Field = keyMapping.SourceField,
                            Error = "Key field is empty"
                        });
                        continue;
                    }

                    // Check required fields
                    bool missingRequired = false;
                    foreach (var mapping in mappings.Where(m => m.IsRequired && m.IsEnabled))
                    {
                        var val = GetSourceValue(record, mapping.SourceField);
                        if (string.IsNullOrWhiteSpace(val) && string.IsNullOrWhiteSpace(mapping.DefaultValue))
                        {
                            result.ErrorList.Add(new HRImportError
                            {
                                Row = rowIdx + 1,
                                Field = mapping.SourceField,
                                Error = $"Required field '{mapping.SourceField}' is empty"
                            });
                            missingRequired = true;
                        }
                    }
                    if (missingRequired)
                    {
                        result.Skipped++;
                        continue;
                    }

                    // Build column values for this record
                    var columns = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var mapping in mappings.Where(m => m.IsEnabled))
                    {
                        var normalizedTarget = NormalizeToColumnName(mapping.TargetField, validColumns);

                        // Remap legacy seed bug: ManagerIdentityId → ManagerEmployeeId
                        if (normalizedTarget.Equals("ManagerIdentityId", StringComparison.OrdinalIgnoreCase))
                            normalizedTarget = "ManagerEmployeeId";

                        if (!validColumns.Contains(normalizedTarget))
                            continue;

                        var rawValue = GetSourceValue(record, mapping.SourceField);
                        var value = ApplyTransformation(rawValue, mapping.Transformation, mapping.DefaultValue);
                        columns[normalizedTarget] = value;
                    }

                    // Validate email format
                    if (columns.TryGetValue("PrimaryEmail", out var emailVal) && emailVal is string email)
                    {
                        if (!string.IsNullOrEmpty(email) && !IsValidEmail(email))
                        {
                            result.ErrorList.Add(new HRImportError
                            {
                                Row = rowIdx + 1,
                                Field = "PrimaryEmail",
                                Error = $"Invalid email format: {email}"
                            });
                            result.Skipped++;
                            continue;
                        }
                    }

                    // Resolve source status if lifecycle config is active
                    string? sourceStatus = null;
                    if (stepConfig?.StatusField != null)
                    {
                        var statusMapping = mappings.FirstOrDefault(m =>
                            m.SourceField.Equals(stepConfig.StatusField, StringComparison.OrdinalIgnoreCase));
                        if (statusMapping != null)
                            sourceStatus = GetSourceValue(record, statusMapping.SourceField);
                        else
                            sourceStatus = GetSourceValue(record, stepConfig.StatusField);
                    }
                    bool isStatusInactive = false;
                    if (sourceStatus != null && stepConfig != null)
                    {
                        if (string.Equals(stepConfig.EvaluationMode, "DateInPast", StringComparison.OrdinalIgnoreCase))
                        {
                            // Date-based: inactive if date is non-empty and <= today
                            if (DateTime.TryParse(sourceStatus, System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out var parsedDate))
                                isStatusInactive = parsedDate.Date <= DateTime.UtcNow.Date;
                        }
                        else
                        {
                            // StringMatch (default)
                            isStatusInactive = stepConfig.InactiveStatusValues
                                .Any(v => v.Equals(sourceStatus, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    // Check if identity exists by the unique key field
                    var existingId = await conn.QueryFirstOrDefaultAsync<Guid?>(
                        $"SELECT Id FROM Identities WHERE [{uniqueIdField}] = @KeyValue",
                        new { KeyValue = keyValue });

                    if (existingId.HasValue)
                    {
                        // Detect field changes before UPDATE - track ALL mapped columns
                        var changedFields = new List<string>();
                        var updatableColumns = columns
                            .Where(c => !c.Key.Equals(uniqueIdField, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        // Store old values for audit logging
                        var oldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                        if (updatableColumns.Count > 0)
                        {
                            var selectCols = string.Join(", ", updatableColumns.Select(c => $"[{ValidateIdentityColumn(c.Key, validColumns)}]"));
                            var currentValues = await conn.QueryFirstOrDefaultAsync(
                                $"SELECT {selectCols} FROM Identities WHERE Id = @Id",
                                new { Id = existingId.Value });

                            if (currentValues != null)
                            {
                                var currentDict = (IDictionary<string, object?>)currentValues;
                                foreach (var col in updatableColumns)
                                {
                                    var currentVal = currentDict.TryGetValue(col.Key, out var cv) ? cv?.ToString() : null;
                                    var newVal = col.Value?.ToString();
                                    if (!string.Equals(currentVal ?? "", newVal ?? "", StringComparison.OrdinalIgnoreCase))
                                    {
                                        changedFields.Add(col.Key);
                                        oldValues[col.Key] = currentVal;
                                    }
                                }
                            }
                        }

                        // UPDATE existing identity
                        var setClauses = new List<string>();
                        var parameters = new DynamicParameters();
                        parameters.Add("Id", existingId.Value);

                        foreach (var col in columns)
                        {
                            if (col.Key.Equals(uniqueIdField, StringComparison.OrdinalIgnoreCase))
                                continue; // Don't update the key field

                            setClauses.Add($"[{ValidateIdentityColumn(col.Key, validColumns)}] = @{col.Key}");
                            parameters.Add(col.Key, col.Value);
                        }

                        // Lifecycle: sync IsActive and Status from source status field
                        if (stepConfig?.SyncIsActiveFromStatus == true && sourceStatus != null)
                        {
                            var newIsActive = !isStatusInactive;
                            if (!setClauses.Any(c => c.Contains("[IsActive]")))
                            {
                                setClauses.Add("[IsActive] = @IsActive");
                                parameters.Add("IsActive", newIsActive);
                            }
                            if (!setClauses.Any(c => c.Contains("[Status]")))
                            {
                                setClauses.Add("[Status] = @Status");
                                parameters.Add("Status", newIsActive ? "Active" : "Inactive");
                            }
                            if (newIsActive) result.Enabled++;
                            else result.Disabled++;
                        }

                        if (setClauses.Count > 0)
                        {
                            setClauses.Add("[ModifiedAt] = @ModifiedAt");
                            parameters.Add("ModifiedAt", DateTime.UtcNow);

                            var updateSql = $"UPDATE Identities SET {string.Join(", ", setClauses)} WHERE Id = @Id";
                            await conn.ExecuteAsync(updateSql, parameters);
                        }
                        result.Updated++;
                        result.UpdatedIdentityIds.Add(existingId.Value);

                        if (changedFields.Count > 0)
                        {
                            var displayName = columns.GetValueOrDefault("DisplayName")?.ToString() ?? "";
                            var empId = keyValue ?? "";

                            // Track lifecycle changes for Joiner/Mover/Leaver events
                            var lifecycleChanges = changedFields.Where(f => lifecycleFields.Contains(f)).ToList();
                            if (lifecycleChanges.Count > 0)
                            {
                                result.UpdatedIdentityChanges.Add(new HRIdentityChange
                                {
                                    IdentityId = existingId.Value,
                                    EmployeeId = empId,
                                    DisplayName = displayName,
                                    ChangedFields = lifecycleChanges
                                });
                            }

                            // Create audit entries for ALL changed fields
                            foreach (var field in changedFields)
                            {
                                auditEntries.Add(new ChangeAuditEntry
                                {
                                    Timestamp = DateTime.UtcNow,
                                    OperationType = ChangeOperationType.Update,
                                    EntityType = "Identity",
                                    EntityId = existingId.Value,
                                    EntityDisplayName = displayName,
                                    PropertyName = field,
                                    OldValue = oldValues.GetValueOrDefault(field),
                                    NewValue = columns.GetValueOrDefault(field)?.ToString(),
                                    Source = "HRImport",
                                    Success = true
                                });
                            }
                        }
                    }
                    else
                    {
                        // INSERT new identity
                        var newId = Guid.NewGuid();
                        columns["Id"] = newId;
                        columns[uniqueIdField] = keyValue;

                        // Auto-generate DisplayName if not mapped
                        if (!columns.ContainsKey("DisplayName") || columns["DisplayName"] == null)
                        {
                            var firstName = columns.GetValueOrDefault("FirstName")?.ToString() ?? "";
                            var lastName = columns.GetValueOrDefault("LastName")?.ToString() ?? "";
                            columns["DisplayName"] = $"{firstName} {lastName}".Trim();
                        }

                        // Lifecycle filter: skip creating inactive identities
                        if (stepConfig?.SkipCreateWhenInactive == true && isStatusInactive)
                        {
                            result.Skipped++;
                            continue;
                        }

                        columns["CreatedAt"] = DateTime.UtcNow;
                        columns["ModifiedAt"] = DateTime.UtcNow;

                        // Lifecycle: derive IsActive from status field instead of hardcoding
                        if (stepConfig?.SyncIsActiveFromStatus == true && sourceStatus != null)
                        {
                            columns["IsActive"] = !isStatusInactive;
                            columns["Status"] = isStatusInactive ? "Inactive" : "Active";
                            if (!isStatusInactive) result.Enabled++;
                            else result.Disabled++;
                        }
                        else
                        {
                            columns["IsActive"] = true;
                            columns["Status"] = "Active";
                            result.Enabled++;
                        }

                        var colNames = columns.Keys.Select(k => $"[{ValidateIdentityColumn(k, validColumns)}]");
                        var paramNames = columns.Keys.Select(k => $"@{k}");
                        var insertSql = $"INSERT INTO Identities ({string.Join(", ", colNames)}) VALUES ({string.Join(", ", paramNames)})";

                        var parameters = new DynamicParameters();
                        foreach (var col in columns)
                            parameters.Add(col.Key, col.Value);

                        await conn.ExecuteAsync(insertSql, parameters);
                        result.Created++;
                        result.CreatedIdentityIds.Add(newId);

                        // Create audit entry for new identity
                        auditEntries.Add(new ChangeAuditEntry
                        {
                            Timestamp = DateTime.UtcNow,
                            OperationType = ChangeOperationType.Create,
                            EntityType = "Identity",
                            EntityId = newId,
                            EntityDisplayName = columns.GetValueOrDefault("DisplayName")?.ToString(),
                            Source = "HRImport",
                            Success = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    result.ErrorList.Add(new HRImportError
                    {
                        Row = rowIdx + 1,
                        Error = ex.Message
                    });
                }
            }

            // Flush audit entries (non-fatal)
            try
            {
                if (auditEntries.Count > 0)
                    await _auditLogService.LogChangesAsync(auditEntries);
            }
            catch { /* Audit logging must not fail the import */ }

            return result;
        }, ct);
    }

    // ========== Helpers ==========

    private static string? GetSourceValue(Dictionary<string, object?> record, string sourceField)
    {
        // Try exact match first, then case-insensitive
        if (record.TryGetValue(sourceField, out var val))
            return val?.ToString();

        var key = record.Keys.FirstOrDefault(k => k.Equals(sourceField, StringComparison.OrdinalIgnoreCase));
        if (key != null && record.TryGetValue(key, out var val2))
            return val2?.ToString();

        return null;
    }

    private static object? ApplyTransformation(string? rawValue, string? transformation, string? defaultValue)
    {
        var value = string.IsNullOrWhiteSpace(rawValue) ? defaultValue : rawValue;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return transformation?.ToLowerInvariant() switch
        {
            "uppercase" => value.ToUpperInvariant(),
            "lowercase" => value.ToLowerInvariant(),
            "titlecase" => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()),
            "trim" => value.Trim(),
            "dateparse" => TryParseDate(value),
            _ => value
        };
    }

    private static object? TryParseDate(string value)
    {
        string[] formats = {
            "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy-MM-ddTHH:mm:ss",
            "M/d/yyyy", "d-MMM-yyyy", "MMM d, yyyy", "yyyy/MM/dd"
        };

        if (DateTime.TryParseExact(value, formats, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt;

        if (DateTime.TryParse(value, out var dt2))
            return dt2;

        return value; // Return as-is if unparseable
    }

    private static bool IsValidEmail(string email)
    {
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    /// <summary>
    /// Normalizes a field name like "Employee ID" or "first_name" to match an actual Identity column.
    /// Strips spaces, underscores, hyphens and does case-insensitive comparison.
    /// </summary>
    private static string NormalizeToColumnName(string fieldName, HashSet<string> validColumns)
    {
        // Direct match first
        if (validColumns.Contains(fieldName))
            return fieldName;

        // Strip spaces/underscores/hyphens and match case-insensitively
        var normalized = fieldName.Replace(" ", "").Replace("_", "").Replace("-", "");
        foreach (var col in validColumns)
        {
            if (col.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return col;
        }

        return fieldName; // Return original if no match found
    }

    // System-managed columns the importer writes directly (never sourced from a mapping/config value).
    private static readonly HashSet<string> _identitySystemColumns = new(StringComparer.OrdinalIgnoreCase)
        { "Id", "CreatedAt", "ModifiedAt", "IsActive", "Status" };

    /// <summary>
    /// Validates a column name before it is interpolated into a SQL identifier position.
    /// Only allow-listed Identity columns (the writable set plus system-managed columns) are permitted;
    /// anything else is rejected to prevent SQL injection via admin-authored field mappings.
    /// </summary>
    private static string ValidateIdentityColumn(string column, HashSet<string> validColumns)
    {
        if (!validColumns.Contains(column) && !_identitySystemColumns.Contains(column))
            throw new InvalidOperationException(
                $"HR Import rejected target column '{column}' — not an allow-listed Identity column.");
        return column;
    }
}
