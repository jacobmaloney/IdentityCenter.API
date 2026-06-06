using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service responsible for transforming AD attributes to Identity model.
    /// Handles attribute mapping, transformations, and Identity property population.
    /// Extracted from SyncProjectOrchestrator for better separation of concerns.
    /// </summary>
    public class AttributeMappingService
    {
        private readonly ILogger<AttributeMappingService> _logger;

        public AttributeMappingService(ILogger<AttributeMappingService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Applies attribute mappings from source object to create IdentityObject.
        /// Handles both IdentityColumn (direct properties) and ExtendedAttribute (ObjectAttribute table) mappings.
        /// </summary>
        // Static counter for one-time diagnostic logging
        private static int _managerDiagLogCount = 0;

        public Task<IdentityObject> ApplyAttributeMappingsAsync(
            Dictionary<string, object> sourceObject,
            SyncStep step,
            SyncProject project,
            CancellationToken cancellationToken)
        {
            // ONE-TIME DIAGNOSTIC: Log first 3 objects to show what attributes we have
            if (_managerDiagLogCount < 3 && step.ObjectClass?.Equals("user", StringComparison.OrdinalIgnoreCase) == true)
            {
                _managerDiagLogCount++;
                var hasManagerAttr = sourceObject.Keys.Any(k => k.Equals("manager", StringComparison.OrdinalIgnoreCase));
                var diagManagerVal = hasManagerAttr ? sourceObject.FirstOrDefault(k => k.Key.Equals("manager", StringComparison.OrdinalIgnoreCase)).Value?.ToString() : null;
                var displayName = sourceObject.ContainsKey("displayName") ? sourceObject["displayName"]?.ToString() : "(no displayName)";
                _logger.LogDebug("🔍 MANAGER DIAGNOSTIC #{Count} for {DisplayName}: HasManagerKey={HasKey}, ManagerValue={Value}, AllKeys=[{Keys}]",
                    _managerDiagLogCount, displayName, hasManagerAttr, diagManagerVal ?? "(null)",
                    string.Join(", ", sourceObject.Keys.Take(30)));
            }

            // Extract raw source ID — support both AD's "objectGuid" and Entra ID's "id"
            var rawSourceId = sourceObject.ContainsKey("objectGuid")
                ? sourceObject["objectGuid"].ToString()!
                : sourceObject.ContainsKey("id")
                    ? sourceObject["id"].ToString()!
                    : throw new KeyNotFoundException("Source object has neither 'objectGuid' nor 'id' key");

            var identityObject = new IdentityObject
            {
                SourceConnectionId = project.SourceConnectionId!.Value,
                // Normalize GUIDs to uppercase for consistent matching (AD objectGuid is case-insensitive).
                // Preserve original case for non-GUID IDs (SharePoint/OneDrive resource IDs like "b!abc..."
                // are base64-encoded and case-sensitive — uppercasing corrupts them).
                SourceUniqueId = Guid.TryParse(rawSourceId, out _) ? rawSourceId.ToUpperInvariant() : rawSourceId,
                SourceType = project.SourceConnection?.ConnectionType ?? "ActiveDirectory", // NULL-safe: fallback if navigation property not loaded
                FirstSyncedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                Attributes = new List<ObjectAttribute>() // Initialize extended attributes collection
            };

            // Determine ObjectClass from LDAP objectClass attribute
            identityObject.ObjectClass = DetermineObjectClass(sourceObject, step);

            // CRITICAL FIX: Populate Attributes collection from ALL source attributes FIRST
            // This ensures we capture all attributes, not just the ones with explicit mappings
            foreach (var kvp in sourceObject)
            {
                var attributeName = kvp.Key;
                var attributeValue = ConvertAttributeValueToString(kvp.Value, attributeName);

                // Skip null/empty values and objectGuid (already stored as SourceUniqueId)
                if (string.IsNullOrWhiteSpace(attributeValue) || attributeName.Equals("objectGuid", StringComparison.OrdinalIgnoreCase))
                    continue;

                identityObject.Attributes.Add(new ObjectAttribute
                {
                    ObjectId = identityObject.Id,
                    AttributeName = attributeName,
                    AttributeValue = attributeValue,
                    DataType = DetermineDataType(kvp.Value),
                    LastSyncedAt = DateTime.UtcNow
                });
            }

            // Apply each attribute mapping
            foreach (var mapping in step.AttributeMappings.Where(m => m.IsEnabled).OrderBy(m => m.ExecutionOrder))
            {
                var sourceValue = GetSourceValue(sourceObject, mapping.SourceAttribute);
                var transformedValue = ApplyTransformation(sourceValue, mapping);

                // Route to appropriate target based on TargetType
                if (mapping.TargetType == "IdentityColumn" || mapping.TargetType == "CoreProperty")
                {
                    // Map to direct IdentityObject table column
                    MapToIdentityProperty(identityObject, mapping.TargetAttribute, transformedValue);
                }
                else if (mapping.TargetType == "ExtendedAttribute")
                {
                    // Map to ObjectAttribute table for flexible extended attributes
                    // NOTE: Already added all attributes above, but if there's a mapping with transformation,
                    // update the existing attribute or add if transformation creates new value
                    if (!string.IsNullOrWhiteSpace(transformedValue))
                    {
                        var existingAttr = identityObject.Attributes.FirstOrDefault(a =>
                            a.AttributeName.Equals(mapping.TargetAttribute, StringComparison.OrdinalIgnoreCase));

                        if (existingAttr != null)
                        {
                            // Update existing attribute with transformed value
                            existingAttr.AttributeValue = transformedValue;
                            existingAttr.DataType = mapping.DataType;
                        }
                        else
                        {
                            // Add new transformed attribute
                            identityObject.Attributes.Add(new ObjectAttribute
                            {
                                AttributeName = mapping.TargetAttribute,
                                AttributeValue = transformedValue,
                                DataType = mapping.DataType,
                                LastSyncedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
                else
                {
                    // Default to IdentityColumn for backward compatibility
                    _logger.LogWarning(
                        "Unknown TargetType '{TargetType}' for mapping '{SourceAttribute}' -> '{TargetAttribute}'. Defaulting to IdentityColumn.",
                        mapping.TargetType, mapping.SourceAttribute, mapping.TargetAttribute);
                    MapToIdentityProperty(identityObject, mapping.TargetAttribute, transformedValue);
                }
            }

            // DISPLAY NAME FALLBACK LOGIC
            // If displayName is missing or empty, use fallback chain: displayName → name → cn → samAccountName → FirstName+LastName
            // CRITICAL FIX: Use GetSourceValue() for case-insensitive attribute lookups (AD returns lowercase keys)
            if (string.IsNullOrWhiteSpace(identityObject.DisplayName))
            {
                // Try 'displayName' attribute directly from source dict (Entra ID groups, service principals, etc.)
                var displayNameValue = ConvertAttributeValueToString(GetSourceValue(sourceObject, "displayName"), "displayName");
                // Try 'name' attribute (used by domain, OU, container, computer, and other non-user objects)
                var nameValue = ConvertAttributeValueToString(GetSourceValue(sourceObject, "name"), "name");
                // Try 'cn' attribute (common name - works for computers, groups, etc.)
                var cnValue = ConvertAttributeValueToString(GetSourceValue(sourceObject, "cn"), "cn");

                if (!string.IsNullOrWhiteSpace(displayNameValue))
                {
                    identityObject.DisplayName = displayNameValue;
                    _logger.LogDebug("Using 'displayName' source attribute as DisplayName fallback: {DisplayName}", identityObject.DisplayName);
                }
                else if (!string.IsNullOrWhiteSpace(nameValue))
                {
                    identityObject.DisplayName = nameValue;
                    _logger.LogDebug("Using 'name' attribute as DisplayName fallback: {Name}", identityObject.DisplayName);
                }
                else if (!string.IsNullOrWhiteSpace(cnValue))
                {
                    identityObject.DisplayName = cnValue;
                    _logger.LogDebug("Using 'cn' attribute as DisplayName fallback: {CN}", identityObject.DisplayName);
                }
                else if (!string.IsNullOrWhiteSpace(identityObject.Username))
                {
                    // For computer accounts, strip trailing $ if present
                    var displayUsername = identityObject.Username;
                    if (displayUsername.EndsWith("$"))
                        displayUsername = displayUsername.TrimEnd('$');
                    identityObject.DisplayName = displayUsername;
                    _logger.LogDebug("Using Username (samAccountName) as DisplayName fallback: {Username}", identityObject.DisplayName);
                }
                else
                {
                    // Try extracting CN from distinguishedName
                    var dn = GetSourceValue(sourceObject, "distinguishedName")?.ToString();
                    var cn = ExtractCnFromDistinguishedName(dn);

                    if (!string.IsNullOrWhiteSpace(cn))
                    {
                        identityObject.DisplayName = cn;
                        _logger.LogDebug("Using CN from distinguishedName as DisplayName fallback: {CN}", cn);
                    }
                    else
                    {
                        // Last resort: use FirstName + LastName (for user objects only)
                        var fullName = $"{identityObject.FirstName} {identityObject.LastName}".Trim();
                        if (!string.IsNullOrWhiteSpace(fullName))
                        {
                            identityObject.DisplayName = fullName;
                            _logger.LogDebug("Using FirstName+LastName as DisplayName fallback: {FullName}", fullName);
                        }
                        else
                        {
                            _logger.LogWarning("No valid DisplayName could be determined for identity {SourceUniqueId} (ObjectClass: {ObjectClass})",
                                identityObject.SourceUniqueId, identityObject.ObjectClass);
                        }
                    }
                }
            }

            // Determine if account is active and other UAC flags (check userAccountControl for AD users)
            // CRITICAL FIX: Use GetSourceValue() for case-insensitive lookup
            var uacValue = GetSourceValue(sourceObject, "userAccountControl");
            if (uacValue != null)
            {
                var uac = Convert.ToInt32(uacValue);
                identityObject.UserAccountControl = uac; // Store raw UAC value for Account tab
                identityObject.IsActive = (uac & 0x0002) == 0; // ADS_UF_ACCOUNTDISABLE
                identityObject.PasswordNeverExpires = (uac & 0x10000) != 0; // ADS_UF_DONT_EXPIRE_PASSWD
            }

            // AUTO-MAP manager attribute to ManagerSourceId column for Manager Resolution step
            // This stores the manager's DN which is later resolved to ManagerObjectId by the Lookup step
            var managerValue = GetSourceValue(sourceObject, "manager");
            if (managerValue != null)
            {
                var managerDn = ConvertAttributeValueToString(managerValue, "manager");
                if (!string.IsNullOrWhiteSpace(managerDn))
                {
                    identityObject.ManagerSourceId = managerDn;
                    _logger.LogDebug("✅ Auto-mapped manager DN to ManagerSourceId for {DisplayName}: {ManagerDN}",
                        identityObject.DisplayName ?? identityObject.Username ?? "(unknown)", managerDn);
                }
            }
            else
            {
                // Log first few objects without manager for debugging (only for user objects)
                if (identityObject.ObjectClass?.Equals("user", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Check if manager key even exists in sourceObject (case variations)
                    var hasManagerKey = sourceObject.Keys.Any(k => k.Equals("manager", StringComparison.OrdinalIgnoreCase));
                    _logger.LogDebug("❌ No manager attribute for user {DisplayName}. HasManagerKey={HasKey}, SourceObjectKeys={Keys}",
                        identityObject.DisplayName ?? identityObject.Username ?? "(unknown)",
                        hasManagerKey,
                        string.Join(", ", sourceObject.Keys.Take(20)));
                }
            }

            // AUTO-MAP distinguishedName to DN column for Manager Resolution matching
            // This ensures all objects have their DN populated so manager lookups work
            // CRITICAL FIX: Use GetSourceValue() for case-insensitive lookup
            var dnValue = GetSourceValue(sourceObject, "distinguishedName")?.ToString();
            if (!string.IsNullOrWhiteSpace(dnValue))
            {
                identityObject.DN = dnValue;
                _logger.LogDebug("✅ Auto-mapped distinguishedName to DN for {DisplayName}: {DN}",
                    identityObject.DisplayName ?? identityObject.Username ?? "(unknown)",
                    dnValue.Length > 80 ? dnValue.Substring(0, 80) + "..." : dnValue);

                // Check if DN contains CN=Builtin (case-insensitive) for built-in account detection
                if (dnValue.IndexOf("CN=Builtin,", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    dnValue.IndexOf(",CN=Builtin,", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    identityObject.IsBuiltIn = true;
                }
            }

            // Also check for well-known built-in accounts by SID
            // Built-in accounts have RID < 1000 (e.g., Administrator=500, Guest=501, KRBTGT=502)
            // Use objectSidBytes which contains the raw byte array
            // CRITICAL FIX: Use GetSourceValue() for case-insensitive lookup
            var sidBytesValue = GetSourceValue(sourceObject, "objectSidBytes");
            if (sidBytesValue != null)
            {
                try
                {
                    var sidBytes = sidBytesValue as byte[];
                    if (sidBytes != null && sidBytes.Length > 0)
                    {
                        // Get RID from SID (last 4 bytes)
                        if (sidBytes.Length >= 8)
                        {
                            var rid = BitConverter.ToUInt32(sidBytes, sidBytes.Length - 4);
                            if (rid < 1000)
                            {
                                identityObject.IsBuiltIn = true;
                                _logger.LogDebug("Detected built-in account by RID={RID} for {DisplayName}",
                                    rid, identityObject.DisplayName ?? "(no display name)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing objectSidBytes for built-in detection");
                }
            }
            else
            {
                // Fallback: Try to parse from string SID if bytes not available
                var sidStringValue = GetSourceValue(sourceObject, "objectSid");
                if (sidStringValue != null)
                {
                    try
                    {
                        var sidString = sidStringValue.ToString();
                        if (!string.IsNullOrWhiteSpace(sidString) && sidString.StartsWith("S-"))
                        {
                            // Extract RID from SID string (last component after final hyphen)
                            // Example: S-1-5-21-domain-domain-domain-500 -> RID is 500
                            var parts = sidString.Split('-');
                            if (parts.Length > 0 && uint.TryParse(parts[^1], out var rid))
                            {
                                if (rid < 1000)
                                {
                                    identityObject.IsBuiltIn = true;
                                    _logger.LogDebug("Detected built-in account by RID={RID} from SID string for {DisplayName}",
                                        rid, identityObject.DisplayName ?? "(no display name)");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing objectSid string for built-in detection");
                    }
                }
            }

            return Task.FromResult(identityObject);
        }

        /// <summary>
        /// Gets a source value from the AD object using case-insensitive attribute lookup.
        /// OPTIMIZED: Uses TryGetValue with common key formats first (fast path), then falls back to iteration.
        /// </summary>
        public object? GetSourceValue(Dictionary<string, object> sourceObject, string attributeName)
        {
            // Fast path 1: Try exact match (most common case when dict already has correct casing)
            if (sourceObject.TryGetValue(attributeName, out var value))
                return value;

            // Fast path 2: Try lowercase (AD often returns lowercase keys)
            if (sourceObject.TryGetValue(attributeName.ToLowerInvariant(), out value))
                return value;

            // Slow path: Case-insensitive search only if needed (rare)
            foreach (var kvp in sourceObject)
            {
                if (kvp.Key.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            return null;
        }

        /// <summary>
        /// Creates a case-insensitive dictionary wrapper for efficient batch lookups.
        /// Call this once per batch and use with direct TryGetValue calls.
        /// </summary>
        public static Dictionary<string, object> CreateCaseInsensitiveDictionary(Dictionary<string, object> sourceObject)
        {
            return new Dictionary<string, object>(sourceObject, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts the CN (Common Name) value from an Active Directory distinguishedName.
        /// Example: "CN=John Doe,OU=Users,DC=example,DC=com" returns "John Doe"
        /// </summary>
        private string? ExtractCnFromDistinguishedName(string? distinguishedName)
        {
            if (string.IsNullOrWhiteSpace(distinguishedName))
            {
                return null;
            }

            try
            {
                // Split by comma but be careful about escaped commas
                // Look for CN= at the start or after a comma
                var cnPrefix = "CN=";
                var startIndex = distinguishedName.IndexOf(cnPrefix, StringComparison.OrdinalIgnoreCase);

                if (startIndex < 0)
                {
                    return null;
                }

                // Find the start of the CN value (after "CN=")
                var valueStart = startIndex + cnPrefix.Length;

                // Find the end of the CN value (next unescaped comma or end of string)
                var valueEnd = distinguishedName.Length;
                for (int i = valueStart; i < distinguishedName.Length; i++)
                {
                    if (distinguishedName[i] == ',' && (i == 0 || distinguishedName[i - 1] != '\\'))
                    {
                        valueEnd = i;
                        break;
                    }
                }

                var cn = distinguishedName.Substring(valueStart, valueEnd - valueStart).Trim();

                // Unescape any escaped characters
                cn = cn.Replace("\\,", ",")
                       .Replace("\\=", "=")
                       .Replace("\\\\", "\\");

                return string.IsNullOrWhiteSpace(cn) ? null : cn;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting CN from distinguishedName: {DN}", distinguishedName);
                return null;
            }
        }

        /// <summary>
        /// Applies a transformation to a source value based on the mapping configuration.
        /// </summary>
        private string? ApplyTransformation(object? sourceValue, AttributeMapping mapping)
        {
            if (sourceValue == null)
            {
                return mapping.DefaultValue;
            }

            var stringValue = ConvertAttributeValueToString(sourceValue, mapping.SourceAttribute);

            return mapping.TransformationType switch
            {
                "Direct" => stringValue,
                "ToUpper" => stringValue?.ToUpper(),
                "ToLower" => stringValue?.ToLower(),
                "Trim" => stringValue?.Trim(),
                _ => stringValue
            };
        }

        /// <summary>
        /// Maps a transformed value to the appropriate IdentityObject property.
        /// </summary>
        private void MapToIdentityProperty(IdentityObject identityObject, string targetAttribute, string? value)
        {
            switch (targetAttribute.ToLower())
            {
                case "sourceuniqueid":
                    // Allow attribute mapping to override hardcoded objectGuid
                    // This enables using objectSid or other attributes as the unique identifier
                    // Only uppercase GUIDs; preserve case for non-GUID IDs (SharePoint resource IDs are case-sensitive)
                    if (value != null)
                        identityObject.SourceUniqueId = Guid.TryParse(value, out _) ? value.ToUpperInvariant() : value;
                    break;
                case "displayname":
                    identityObject.DisplayName = value;
                    break;
                case "email":
                    identityObject.Email = value;
                    break;
                case "username":
                    identityObject.Username = value;
                    break;
                case "firstname":
                    identityObject.FirstName = value;
                    break;
                case "lastname":
                    identityObject.LastName = value;
                    break;
                case "department":
                    identityObject.Department = value;
                    break;
                case "division":
                    identityObject.Division = value;
                    break;
                case "company":
                    identityObject.Company = value;
                    break;
                case "office":
                    identityObject.Office = value;
                    break;
                case "costcenter":
                    identityObject.CostCenter = value;
                    break;
                case "jobtitle":
                    identityObject.JobTitle = value;
                    break;
                case "phone":
                case "phonenumber":
                    identityObject.Phone = value;
                    break;
                case "mobilephone":
                    identityObject.MobilePhone = value;
                    break;
                case "employeeid":
                    identityObject.EmployeeId = value;
                    break;
                case "cn":
                    identityObject.CN = value;
                    break;
                case "dn":
                    identityObject.DN = value;
                    break;
                case "upn":
                case "userprincipalname":
                    identityObject.UserPrincipalName = value;
                    break;
                case "managersourceid":
                case "manager":
                    // Store manager DN for later resolution by Lookup step
                    identityObject.ManagerSourceId = value;
                    break;
                case "managerobjectid":
                    // Resolved manager Object ID (from DN lookup transformation)
                    if (!string.IsNullOrEmpty(value) && Guid.TryParse(value, out var managerId))
                    {
                        identityObject.ManagerObjectId = managerId;
                    }
                    break;
            }
        }

        /// <summary>
        /// Determines the ObjectClass for an identity based on LDAP objectClass attribute and step configuration.
        /// Returns standardized values: User, Computer, Group, Contact, OrganizationalUnit, etc.
        /// Priority: Step.ObjectClass → LDAP objectClass attribute → Username heuristics
        /// </summary>
        private string? DetermineObjectClass(Dictionary<string, object> sourceObject, SyncStep step)
        {
            // Priority 1: ALWAYS use step configuration if specified
            // This ensures we only get the ObjectClass the step is configured for
            if (!string.IsNullOrWhiteSpace(step.ObjectClass))
            {
                return step.ObjectClass.ToLowerInvariant();
            }

            // Priority 2: Extract from LDAP objectClass attribute
            // CRITICAL FIX: Use GetSourceValue() for case-insensitive lookup
            var objectClasses = new List<string>();
            var objectClassValue = GetSourceValue(sourceObject, "objectClass");

            if (objectClassValue != null)
            {
                // Handle both string[] and single string values
                if (objectClassValue is string[] classArray)
                {
                    objectClasses.AddRange(classArray.Where(c => !string.IsNullOrWhiteSpace(c)));
                }
                else if (objectClassValue is string classString && !string.IsNullOrWhiteSpace(classString))
                {
                    objectClasses.Add(classString);
                }
            }

            // Determine the most specific class from the hierarchy
            // LDAP objectClass is ordered from least specific (top) to most specific (user/computer/group)
            // Priority order: computer → contact → group → user → organizationalUnit → person
            // FIXED: Return lowercase values to match UI queries
            if (objectClasses.Any(c => c.Equals("computer", StringComparison.OrdinalIgnoreCase)))
                return "computer";

            if (objectClasses.Any(c => c.Equals("contact", StringComparison.OrdinalIgnoreCase)))
                return "contact";

            if (objectClasses.Any(c => c.Equals("group", StringComparison.OrdinalIgnoreCase)))
                return "group";

            if (objectClasses.Any(c => c.Equals("user", StringComparison.OrdinalIgnoreCase)))
            {
                // Distinguish between user and computer accounts
                // Computer accounts have samAccountName ending with $
                // CRITICAL FIX: Use GetSourceValue() for case-insensitive lookup
                var samAccountName = GetSourceValue(sourceObject, "sAMAccountName")?.ToString();
                if (samAccountName?.EndsWith("$") == true)
                    return "computer";

                return "user";
            }

            if (objectClasses.Any(c => c.Equals("organizationalUnit", StringComparison.OrdinalIgnoreCase)))
                return "organizationalUnit";

            if (objectClasses.Any(c => c.Equals("container", StringComparison.OrdinalIgnoreCase)))
                return "container";
            if (objectClasses.Any(c => c.Equals("domainDNS", StringComparison.OrdinalIgnoreCase)))
                return "domainDNS";


            // Priority 3: Fallback heuristics if objectClass is not available or unclear
            // CRITICAL FIX: Use GetSourceValue() for case-insensitive lookup
            var fallbackSamAccountName = GetSourceValue(sourceObject, "sAMAccountName")?.ToString();
            if (fallbackSamAccountName?.EndsWith("$") == true)
            {
                return "computer";
            }

            // Default to step configuration or "user" as ultimate fallback
            return step.ObjectClass ?? "user";
        }

        /// <summary>
        /// CRITICAL FIX: Determines the data type of an attribute value for storage.
        /// </summary>
        private string DetermineDataType(object? value)
        {
            if (value == null) return "String";

            return value.GetType().Name switch
            {
                "Int32" => "Integer",
                "Int64" => "Long",
                "Boolean" => "Boolean",
                "DateTime" => "DateTime",
                "Byte[]" => "Binary",
                _ => "String"
            };
        }

        /// <summary>
        /// Converts attribute values to string, properly handling arrays.
        /// For objectClass: returns the LAST (most specific) value (e.g., "organizationalUnit" not "top")
        /// For other arrays: joins values with semicolon
        /// </summary>
        private string? ConvertAttributeValueToString(object? value, string attributeName)
        {
            if (value == null) return null;

            // Handle string arrays (common in LDAP attributes like objectClass, memberOf)
            if (value is string[] stringArray && stringArray.Length > 0)
            {
                // For objectClass, return the LAST (most specific) value
                // AD returns objectClass as ["top", "person", "organizationalUnit"] - we want "organizationalUnit"
                if (attributeName.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
                {
                    return stringArray.Last();
                }
                // For other multi-valued attributes, join with semicolon
                return string.Join(";", stringArray);
            }

            // Handle object arrays
            if (value is object[] objArray && objArray.Length > 0)
            {
                if (attributeName.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
                {
                    return objArray.Last()?.ToString();
                }
                return string.Join(";", objArray.Select(o => o?.ToString() ?? ""));
            }

            // Handle IEnumerable<string>
            if (value is IEnumerable<string> stringEnumerable)
            {
                var list = stringEnumerable.ToList();
                if (list.Count > 0)
                {
                    if (attributeName.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
                    {
                        return list.Last();
                    }
                    return string.Join(";", list);
                }
            }

            // Default: use ToString()
            return value.ToString();
        }
    }
}
