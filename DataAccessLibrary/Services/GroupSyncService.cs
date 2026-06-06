using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using DataAccessLibrary.Models;
using DataAccessLibrary.Data;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service responsible for syncing Group objects from directory sources.
    /// Handles attribute mapping and persistence to the Groups table.
    /// </summary>
    public class GroupSyncService
    {
        private readonly ILogger<GroupSyncService> _logger;

        public GroupSyncService(ILogger<GroupSyncService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Applies attribute mappings from source object to create Group.
        /// </summary>
        public Task<Group> ApplyAttributeMappingsAsync(
            Dictionary<string, object> sourceObject,
            SyncStep step,
            SyncProject project,
            string sourceType,
            CancellationToken cancellationToken)
        {
            var group = new Group
            {
                SourceConnectionId = project.SourceConnectionId!.Value,
                SourceUniqueId = sourceObject["objectGuid"].ToString()!,
                SourceType = sourceType,
                FirstSyncedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                Attributes = new List<GroupAttribute>() // CRITICAL FIX: Initialize attributes collection
            };

            // CRITICAL FIX: Populate Attributes collection from ALL source attributes
            foreach (var kvp in sourceObject)
            {
                var attributeName = kvp.Key;
                var attributeValue = kvp.Value?.ToString();

                // Skip null/empty values and objectGuid (already stored as SourceUniqueId)
                if (string.IsNullOrWhiteSpace(attributeValue) || attributeName.Equals("objectGuid", StringComparison.OrdinalIgnoreCase))
                    continue;

                group.Attributes.Add(new GroupAttribute
                {
                    // NOTE: GroupId will be set by the orchestrator after group is upserted
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

                // Map to Group properties
                MapToGroupProperty(group, mapping.TargetAttribute, transformedValue, sourceObject);
            }

            // Ensure name is populated
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                // Fallback: extract CN from distinguishedName
                if (sourceObject.ContainsKey("distinguishedName"))
                {
                    var dn = sourceObject["distinguishedName"]?.ToString();
                    var cn = ExtractCnFromDistinguishedName(dn);
                    if (!string.IsNullOrWhiteSpace(cn))
                    {
                        group.Name = cn;
                    }
                }
            }

            // AUTO-POPULATE DistinguishedName from source (CRITICAL for membership lookup)
            if (string.IsNullOrWhiteSpace(group.DistinguishedName) && sourceObject.ContainsKey("distinguishedName"))
            {
                group.DistinguishedName = sourceObject["distinguishedName"]?.ToString();
            }

            // Determine if group is mail-enabled
            if (!string.IsNullOrWhiteSpace(group.Email))
            {
                group.IsMailEnabled = true;
            }

            // Determine group type from groupType attribute
            if (sourceObject.ContainsKey("groupType"))
            {
                var groupTypeValue = Convert.ToInt32(sourceObject["groupType"]);
                // AD groupType: -2147483646 = Global Security, -2147483644 = Domain Local Security
                // -2147483640 = Universal Security, 2 = Global Distribution, 4 = Domain Local Distribution, 8 = Universal Distribution
                if ((groupTypeValue & 0x80000000) != 0) // High bit set = Security group
                {
                    group.GroupType = "Security";
                }
                else
                {
                    group.GroupType = "Distribution";
                }
            }

            // Check if group is active (no specific attribute, assume active if not deleted)
            group.IsActive = true;

            return Task.FromResult(group);
        }

        /// <summary>
        /// Gets a source value from the AD object using case-insensitive attribute lookup.
        /// </summary>
        private object? GetSourceValue(Dictionary<string, object> sourceObject, string attributeName)
        {
            var key = sourceObject.Keys.FirstOrDefault(k => k.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
            return key != null ? sourceObject[key] : null;
        }

        /// <summary>
        /// Extracts the CN (Common Name) value from an Active Directory distinguishedName.
        /// </summary>
        private string? ExtractCnFromDistinguishedName(string? distinguishedName)
        {
            if (string.IsNullOrWhiteSpace(distinguishedName))
                return null;

            try
            {
                var cnPrefix = "CN=";
                var startIndex = distinguishedName.IndexOf(cnPrefix, StringComparison.OrdinalIgnoreCase);

                if (startIndex < 0)
                    return null;

                var valueStart = startIndex + cnPrefix.Length;
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
                cn = cn.Replace("\\,", ",").Replace("\\=", "=").Replace("\\\\", "\\");

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
                return mapping.DefaultValue;

            var stringValue = sourceValue.ToString();

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
        /// Maps a transformed value to the appropriate Group property.
        /// </summary>
        private void MapToGroupProperty(Group group, string targetAttribute, string? value, Dictionary<string, object> sourceObject)
        {
            switch (targetAttribute.ToLower())
            {
                case "name":
                    group.Name = value ?? group.Name;
                    break;
                case "description":
                    group.Description = value;
                    break;
                case "email":
                case "mail":
                    group.Email = value;
                    break;
                case "distinguishedname":
                    group.DistinguishedName = value;
                    break;
                case "grouptype":
                    group.GroupType = value;
                    break;
                case "managedby":
                    group.ManagedBy = value;  // Store the owner DN for later resolution
                    break;
            }
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
    }
}
