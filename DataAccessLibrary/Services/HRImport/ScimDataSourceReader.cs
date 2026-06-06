using System.Net.Http.Headers;
using System.Text.Json;
using DataAccessLibrary.Models;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services.HRImport;

/// <summary>
/// Reads HR data from SCIM 2.0 /Users endpoint.
/// Maps SCIM User schema to flat dictionary for Identity table import.
/// Handles pagination via startIndex/count.
/// </summary>
public class ScimDataSourceReader : IHRDataSourceReader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScimDataSourceReader> _logger;

    public string SourceType => "SCIM";

    public ScimDataSourceReader(
        IHttpClientFactory httpClientFactory,
        ILogger<ScimDataSourceReader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HRDataReadResult> ReadAsync(
        DirectoryConnection connection,
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default)
    {
        var result = new HRDataReadResult();

        if (string.IsNullOrWhiteSpace(config.ScimEndpoint))
        {
            result.ErrorMessage = "SCIM endpoint URL is not configured.";
            return result;
        }

        try
        {
            var client = CreateAuthenticatedClient(credentials);
            var allRecords = new List<Dictionary<string, object?>>();
            int startIndex = 1;
            int count = config.ImportBatchSize > 0 ? config.ImportBatchSize : 100;
            int totalResults = int.MaxValue;

            while (startIndex <= totalResults)
            {
                ct.ThrowIfCancellationRequested();

                var url = $"{config.ScimEndpoint.TrimEnd('/')}/Users?startIndex={startIndex}&count={count}";
                var response = await client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Get totalResults from SCIM response
                if (root.TryGetProperty("totalResults", out var totalProp))
                    totalResults = totalProp.GetInt32();

                // Parse Resources array
                if (root.TryGetProperty("Resources", out var resources) &&
                    resources.ValueKind == JsonValueKind.Array)
                {
                    foreach (var user in resources.EnumerateArray())
                    {
                        var record = MapScimUserToRecord(user);
                        allRecords.Add(record);
                    }
                }
                else
                {
                    break; // No more resources
                }

                startIndex += count;
                _logger.LogInformation("SCIM page: fetched {Count}/{Total} records",
                    allRecords.Count, totalResults);
            }

            result.Records = allRecords;
            result.TotalRecords = allRecords.Count;
            result.FieldNames = GetScimFieldNames();

            _logger.LogInformation("SCIM read complete: {Count} records from {Endpoint}",
                result.TotalRecords, config.ScimEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read from SCIM endpoint: {Endpoint}", config.ScimEndpoint);
            result.ErrorMessage = $"SCIM read error: {ex.Message}";
        }

        return result;
    }

    public Task<List<string>> GetAvailableFieldsAsync(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default)
    {
        // SCIM has a well-known schema
        return Task.FromResult(GetScimFieldNames());
    }

    public async Task<bool> TestConnectionAsync(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.ScimEndpoint))
            return false;

        try
        {
            var client = CreateAuthenticatedClient(credentials);
            var url = $"{config.ScimEndpoint.TrimEnd('/')}/Users?startIndex=1&count=1";
            var response = await client.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SCIM test connection failed");
            return false;
        }
    }

    private HttpClient CreateAuthenticatedClient(HRCredentials credentials)
    {
        var client = _httpClientFactory.CreateClient("HRImportScim");
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/scim+json"));

        if (!string.IsNullOrEmpty(credentials.BearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", credentials.BearerToken);
        }
        else if (!string.IsNullOrEmpty(credentials.Username) && !string.IsNullOrEmpty(credentials.Password))
        {
            var encoded = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{credentials.Username}:{credentials.Password}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", encoded);
        }

        return client;
    }

    /// <summary>
    /// Maps a SCIM 2.0 User resource to a flat dictionary.
    /// </summary>
    private static Dictionary<string, object?> MapScimUserToRecord(JsonElement user)
    {
        var record = new Dictionary<string, object?>();

        // Core SCIM attributes
        record["id"] = GetJsonString(user, "id");
        record["externalId"] = GetJsonString(user, "externalId");
        record["userName"] = GetJsonString(user, "userName");
        record["active"] = user.TryGetProperty("active", out var active) ? active.GetBoolean().ToString() : null;

        // Name
        if (user.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.Object)
        {
            record["name.givenName"] = GetJsonString(name, "givenName");
            record["name.familyName"] = GetJsonString(name, "familyName");
            record["name.middleName"] = GetJsonString(name, "middleName");
            record["name.honorificPrefix"] = GetJsonString(name, "honorificPrefix");
            record["name.honorificSuffix"] = GetJsonString(name, "honorificSuffix");
            record["name.formatted"] = GetJsonString(name, "formatted");
        }

        // Emails (primary or first)
        if (user.TryGetProperty("emails", out var emails) && emails.ValueKind == JsonValueKind.Array)
        {
            var primaryEmail = FindPrimaryOrFirst(emails);
            if (primaryEmail != null)
                record["emails.value"] = GetJsonString(primaryEmail.Value, "value");
        }

        // Phone numbers
        if (user.TryGetProperty("phoneNumbers", out var phones) && phones.ValueKind == JsonValueKind.Array)
        {
            foreach (var phone in phones.EnumerateArray())
            {
                var type = GetJsonString(phone, "type")?.ToLowerInvariant() ?? "work";
                record[$"phoneNumbers.{type}"] = GetJsonString(phone, "value");
            }
        }

        // Addresses (primary or first)
        if (user.TryGetProperty("addresses", out var addresses) && addresses.ValueKind == JsonValueKind.Array)
        {
            var addr = FindPrimaryOrFirst(addresses);
            if (addr != null)
            {
                record["addresses.streetAddress"] = GetJsonString(addr.Value, "streetAddress");
                record["addresses.locality"] = GetJsonString(addr.Value, "locality");
                record["addresses.region"] = GetJsonString(addr.Value, "region");
                record["addresses.postalCode"] = GetJsonString(addr.Value, "postalCode");
                record["addresses.country"] = GetJsonString(addr.Value, "country");
            }
        }

        // Title
        record["title"] = GetJsonString(user, "title");

        // Enterprise User extension (department, employeeNumber, manager, etc.)
        var enterpriseUrn = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User";
        if (user.TryGetProperty(enterpriseUrn, out var enterprise) && enterprise.ValueKind == JsonValueKind.Object)
        {
            record["enterprise.employeeNumber"] = GetJsonString(enterprise, "employeeNumber");
            record["enterprise.costCenter"] = GetJsonString(enterprise, "costCenter");
            record["enterprise.organization"] = GetJsonString(enterprise, "organization");
            record["enterprise.division"] = GetJsonString(enterprise, "division");
            record["enterprise.department"] = GetJsonString(enterprise, "department");

            if (enterprise.TryGetProperty("manager", out var manager) && manager.ValueKind == JsonValueKind.Object)
            {
                record["enterprise.manager.value"] = GetJsonString(manager, "value");
                record["enterprise.manager.displayName"] = GetJsonString(manager, "displayName");
            }
        }

        // Display name
        record["displayName"] = GetJsonString(user, "displayName");
        record["nickName"] = GetJsonString(user, "nickName");
        record["preferredLanguage"] = GetJsonString(user, "preferredLanguage");
        record["locale"] = GetJsonString(user, "locale");
        record["timezone"] = GetJsonString(user, "timezone");

        return record;
    }

    private static string? GetJsonString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (element.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }

    private static JsonElement? FindPrimaryOrFirst(JsonElement array)
    {
        JsonElement? first = null;
        foreach (var item in array.EnumerateArray())
        {
            first ??= item;
            if (item.TryGetProperty("primary", out var primary) && primary.GetBoolean())
                return item;
        }
        return first;
    }

    private static List<string> GetScimFieldNames()
    {
        return new List<string>
        {
            "id", "externalId", "userName", "active",
            "name.givenName", "name.familyName", "name.middleName",
            "name.honorificPrefix", "name.honorificSuffix", "name.formatted",
            "displayName", "nickName", "title",
            "emails.value",
            "phoneNumbers.work", "phoneNumbers.mobile", "phoneNumbers.home",
            "addresses.streetAddress", "addresses.locality", "addresses.region",
            "addresses.postalCode", "addresses.country",
            "preferredLanguage", "locale", "timezone",
            "enterprise.employeeNumber", "enterprise.costCenter",
            "enterprise.organization", "enterprise.division", "enterprise.department",
            "enterprise.manager.value", "enterprise.manager.displayName"
        };
    }
}
