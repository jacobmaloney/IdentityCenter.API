using System.Net.Http.Headers;
using System.Text.Json;
using DataAccessLibrary.Models;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services.HRImport;

/// <summary>
/// Reads HR data from REST API endpoints.
/// Supports Bearer token, API key, basic auth, and OAuth2 client credentials.
/// Handles pagination via offset, cursor, or Link header.
/// </summary>
public class RestApiDataSourceReader : IHRDataSourceReader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RestApiDataSourceReader> _logger;

    public string SourceType => "RESTAPI";

    public RestApiDataSourceReader(
        IHttpClientFactory httpClientFactory,
        ILogger<RestApiDataSourceReader> logger)
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

        try
        {
            var client = await CreateAuthenticatedClient(config, credentials, ct);
            var allRecords = new List<Dictionary<string, object?>>();
            string? nextUrl = BuildUrl(config);
            int page = 0;

            while (!string.IsNullOrEmpty(nextUrl))
            {
                ct.ThrowIfCancellationRequested();
                page++;

                var request = new HttpRequestMessage(
                    ParseMethod(config.HttpMethod), nextUrl);

                // Add custom headers
                if (config.Headers != null)
                {
                    foreach (var header in config.Headers)
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                var response = await client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(json);

                // Navigate to the data array using DataPath
                var dataElement = NavigateToDataPath(doc.RootElement, config.DataPath);

                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataElement.EnumerateArray())
                    {
                        var record = FlattenJsonElement(item);
                        allRecords.Add(record);
                    }
                }
                else
                {
                    _logger.LogWarning("Expected array at DataPath '{DataPath}', got {Kind}", config.DataPath, dataElement.ValueKind);
                    break;
                }

                // Handle pagination
                nextUrl = GetNextPageUrl(config, response, doc.RootElement, nextUrl, page);

                _logger.LogInformation("REST API page {Page}: fetched {Count} records", page, allRecords.Count);
            }

            result.Records = allRecords;
            result.TotalRecords = allRecords.Count;

            // Extract field names from first record
            if (allRecords.Count > 0)
                result.FieldNames = allRecords[0].Keys.ToList();

            _logger.LogInformation("REST API read complete: {Count} records from {Url}",
                result.TotalRecords, config.ApiBaseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read from REST API: {Url}", config.ApiBaseUrl);
            result.ErrorMessage = $"REST API read error: {ex.Message}";
        }

        return result;
    }

    public async Task<List<string>> GetAvailableFieldsAsync(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default)
    {
        // Read first page and return field names
        var tempConfig = new HRConnectionConfig
        {
            SourceType = config.SourceType,
            ApiBaseUrl = config.ApiBaseUrl,
            ApiEndpoint = config.ApiEndpoint,
            HttpMethod = config.HttpMethod,
            ResponseFormat = config.ResponseFormat,
            DataPath = config.DataPath,
            Headers = config.Headers,
            PaginationType = "None", // Only read first page
            PageSize = 1
        };

        var result = await ReadAsync(new DirectoryConnection(), tempConfig, credentials, ct);
        return result.FieldNames;
    }

    public async Task<bool> TestConnectionAsync(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default)
    {
        try
        {
            var client = await CreateAuthenticatedClient(config, credentials, ct);
            var url = BuildUrl(config);
            var response = await client.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "REST API test connection failed");
            return false;
        }
    }

    private async Task<HttpClient> CreateAuthenticatedClient(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("HRImport");
        client.Timeout = TimeSpan.FromSeconds(60);

        // OAuth2 client credentials flow
        if (!string.IsNullOrEmpty(credentials.ClientId) && !string.IsNullOrEmpty(credentials.ClientSecret)
            && !string.IsNullOrEmpty(credentials.TokenEndpoint))
        {
            var tokenClient = _httpClientFactory.CreateClient("HRImportToken");
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret
            });

            var tokenResponse = await tokenClient.PostAsync(credentials.TokenEndpoint, tokenRequest, ct);
            tokenResponse.EnsureSuccessStatusCode();

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(ct);
            var tokenDoc = JsonDocument.Parse(tokenJson);
            var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }
        else if (!string.IsNullOrEmpty(credentials.BearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", credentials.BearerToken);
        }
        else if (!string.IsNullOrEmpty(credentials.ApiKey))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", credentials.ApiKey);
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

    private static string BuildUrl(HRConnectionConfig config)
    {
        var baseUrl = config.ApiBaseUrl?.TrimEnd('/') ?? "";
        var endpoint = config.ApiEndpoint?.TrimStart('/') ?? "";
        return string.IsNullOrEmpty(endpoint) ? baseUrl : $"{baseUrl}/{endpoint}";
    }

    private static HttpMethod ParseMethod(string method)
    {
        return method?.ToUpperInvariant() switch
        {
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            _ => HttpMethod.Get
        };
    }

    private static JsonElement NavigateToDataPath(JsonElement root, string? dataPath)
    {
        if (string.IsNullOrWhiteSpace(dataPath))
            return root;

        var current = root;
        foreach (var segment in dataPath.Split('.'))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var child))
                current = child;
            else
                return current;
        }
        return current;
    }

    private static Dictionary<string, object?> FlattenJsonElement(JsonElement element, string prefix = "")
    {
        var record = new Dictionary<string, object?>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var nested in FlattenJsonElement(prop.Value, key))
                        record[nested.Key] = nested.Value;
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
                {
                    // Flatten first array element for simple cases (e.g., emails[0].value)
                    var firstItem = prop.Value[0];
                    if (firstItem.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var nested in FlattenJsonElement(firstItem, key))
                            record[nested.Key] = nested.Value;
                    }
                    else
                    {
                        record[key] = firstItem.ToString();
                    }
                }
                else
                {
                    record[key] = GetJsonValue(prop.Value);
                }
            }
        }

        return record;
    }

    private static object? GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal().ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private string? GetNextPageUrl(
        HRConnectionConfig config, HttpResponseMessage response,
        JsonElement root, string currentUrl, int page)
    {
        switch (config.PaginationType?.ToLowerInvariant())
        {
            case "offset":
            {
                var dataElement = NavigateToDataPath(root, config.DataPath);
                if (dataElement.ValueKind != JsonValueKind.Array || dataElement.GetArrayLength() < config.PageSize)
                    return null;

                var offset = page * config.PageSize;
                var separator = currentUrl.Contains('?') ? '&' : '?';
                var baseUrl = BuildUrl(config);
                return $"{baseUrl}{separator}offset={offset}&limit={config.PageSize}";
            }

            case "cursor":
            {
                if (root.TryGetProperty("next_cursor", out var cursor) ||
                    root.TryGetProperty("cursor", out cursor) ||
                    root.TryGetProperty("nextPageToken", out cursor))
                {
                    var cursorValue = cursor.GetString();
                    if (string.IsNullOrEmpty(cursorValue)) return null;
                    var separator = BuildUrl(config).Contains('?') ? '&' : '?';
                    return $"{BuildUrl(config)}{separator}cursor={cursorValue}";
                }
                return null;
            }

            case "linkheader":
            {
                if (response.Headers.TryGetValues("Link", out var linkValues))
                {
                    var linkHeader = string.Join(",", linkValues);
                    var nextMatch = System.Text.RegularExpressions.Regex.Match(
                        linkHeader, @"<([^>]+)>;\s*rel=""next""");
                    if (nextMatch.Success)
                        return nextMatch.Groups[1].Value;
                }
                return null;
            }

            default:
                return null;
        }
    }
}
