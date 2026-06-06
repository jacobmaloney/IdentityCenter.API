using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using DataAccessLibrary.Models;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services.HRImport;

/// <summary>
/// Reads HR data from CSV files uploaded to the server.
/// Uses CsvHelper for robust parsing with configurable delimiter, encoding, and header detection.
/// </summary>
public class CsvDataSourceReader : IHRDataSourceReader
{
    private readonly ILogger<CsvDataSourceReader> _logger;

    public string SourceType => "CSV";

    public CsvDataSourceReader(ILogger<CsvDataSourceReader> logger)
    {
        _logger = logger;
    }

    public async Task<HRDataReadResult> ReadAsync(
        DirectoryConnection connection,
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default)
    {
        var result = new HRDataReadResult();

        if (string.IsNullOrWhiteSpace(config.FileUploadPath))
        {
            result.ErrorMessage = "No CSV file path configured. Upload a file first.";
            return result;
        }

        if (!File.Exists(config.FileUploadPath))
        {
            result.ErrorMessage = $"CSV file not found: {config.FileUploadPath}";
            return result;
        }

        try
        {
            var encoding = GetEncoding(config.Encoding);
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = config.Delimiter ?? ",",
                HasHeaderRecord = config.HasHeaderRow,
                MissingFieldFound = null,
                BadDataFound = null,
                TrimOptions = TrimOptions.Trim
            };

            using var reader = new StreamReader(config.FileUploadPath, encoding);
            using var csv = new CsvReader(reader, csvConfig);

            await csv.ReadAsync();
            csv.ReadHeader();

            if (csv.HeaderRecord != null)
            {
                result.FieldNames = csv.HeaderRecord.ToList();
            }

            while (await csv.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();

                var record = new Dictionary<string, object?>();
                for (int i = 0; i < (csv.HeaderRecord?.Length ?? 0); i++)
                {
                    var header = csv.HeaderRecord![i];
                    var value = csv.GetField(i);
                    record[header] = string.IsNullOrWhiteSpace(value) ? null : value;
                }
                result.Records.Add(record);
            }

            result.TotalRecords = result.Records.Count;
            _logger.LogInformation("CSV read complete: {Count} records from {File}",
                result.TotalRecords, Path.GetFileName(config.FileUploadPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read CSV file: {File}", config.FileUploadPath);
            result.ErrorMessage = $"CSV read error: {ex.Message}";
        }

        return result;
    }

    public async Task<List<string>> GetAvailableFieldsAsync(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.FileUploadPath) || !File.Exists(config.FileUploadPath))
            return new List<string>();

        try
        {
            var encoding = GetEncoding(config.Encoding);
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = config.Delimiter ?? ",",
                HasHeaderRecord = config.HasHeaderRow
            };

            using var reader = new StreamReader(config.FileUploadPath, encoding);
            using var csv = new CsvReader(reader, csvConfig);
            await csv.ReadAsync();
            csv.ReadHeader();
            return csv.HeaderRecord?.ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read CSV headers");
            return new List<string>();
        }
    }

    public async Task<bool> TestConnectionAsync(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.FileUploadPath))
            return false;

        if (!File.Exists(config.FileUploadPath))
            return false;

        try
        {
            var encoding = GetEncoding(config.Encoding);
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = config.Delimiter ?? ",",
                HasHeaderRecord = config.HasHeaderRow,
                MissingFieldFound = null,
                BadDataFound = null
            };

            using var reader = new StreamReader(config.FileUploadPath, encoding);
            using var csv = new CsvReader(reader, csvConfig);

            // Read and validate header row
            if (!await csv.ReadAsync())
                return false;

            csv.ReadHeader();
            if (csv.HeaderRecord == null || csv.HeaderRecord.Length == 0)
                return false;

            // Confirm at least 1 data row exists
            if (!await csv.ReadAsync())
                return false;

            _logger.LogInformation("CSV test connection passed: {ColumnCount} columns, file {File}",
                csv.HeaderRecord.Length, Path.GetFileName(config.FileUploadPath));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CSV test connection failed for file: {File}", config.FileUploadPath);
            return false;
        }
    }

    private static Encoding GetEncoding(string? encodingName)
    {
        return encodingName?.ToUpperInvariant() switch
        {
            "UTF-8" or "UTF8" => Encoding.UTF8,
            "ASCII" => Encoding.ASCII,
            "UTF-16" or "UNICODE" => Encoding.Unicode,
            "UTF-32" => Encoding.UTF32,
            "LATIN1" or "ISO-8859-1" => Encoding.Latin1,
            _ => Encoding.UTF8
        };
    }
}
