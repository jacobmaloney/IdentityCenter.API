using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.HRImport;

/// <summary>
/// Interface for reading records from an HR data source (CSV, REST API, SCIM).
/// Each implementation handles a specific source type.
/// </summary>
public interface IHRDataSourceReader
{
    /// <summary>The source type this reader handles (CSV, RESTAPI, SCIM)</summary>
    string SourceType { get; }

    /// <summary>
    /// Read all records from the HR data source.
    /// Returns flat dictionaries keyed by field name.
    /// </summary>
    Task<HRDataReadResult> ReadAsync(
        DirectoryConnection connection,
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default);

    /// <summary>
    /// Get the list of available field names from the source.
    /// Used by the field mapping editor to populate the source column.
    /// </summary>
    Task<List<string>> GetAvailableFieldsAsync(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default);

    /// <summary>
    /// Test the connection to the HR data source.
    /// Returns true if the source is reachable and readable.
    /// </summary>
    Task<bool> TestConnectionAsync(
        HRConnectionConfig config,
        HRCredentials credentials,
        CancellationToken ct = default);
}
