using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Base class for Dapper-based seed services.
/// Provides shared connection/transaction management and bulk insert helpers.
/// Target: Sub-30-second full database seed (vs ~20 minutes with EF Core).
/// </summary>
public abstract class DapperSeedServiceBase
{
    protected readonly string _connectionString;
    protected readonly ILogger _logger;

    protected DapperSeedServiceBase(
        IConfiguration configuration,
        ILogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Override to implement the actual seeding logic.
    /// Called by the orchestrator with a shared connection and transaction.
    /// </summary>
    public abstract Task SeedAsync(SqlConnection connection, SqlTransaction transaction);

    /// <summary>
    /// Standalone seed method - creates its own connection/transaction.
    /// Use this when seeding a single service without the orchestrator.
    /// </summary>
    public async Task SeedStandaloneAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = connection.BeginTransaction();
        try
        {
            await SeedAsync(connection, transaction);
            await transaction.CommitAsync();
        }
        catch
        {
            try { await transaction.RollbackAsync(); }
            catch { /* transaction may already be completed/rolled back */ }
            throw;
        }
    }

    /// <summary>
    /// Checks if the table has any rows.
    /// Use for idempotent seeding - skip if data exists.
    /// </summary>
    protected async Task<bool> TableHasDataAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tableName)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM [{tableName}]",
            transaction: transaction);
        return count > 0;
    }

    /// <summary>
    /// Gets count of rows matching a condition.
    /// Use for more granular existence checks.
    /// </summary>
    protected async Task<int> GetCountAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tableName,
        string? whereClause = null,
        object? parameters = null)
    {
        var sql = $"SELECT COUNT(*) FROM [{tableName}]";
        if (!string.IsNullOrEmpty(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }

        return await connection.ExecuteScalarAsync<int>(sql, parameters, transaction);
    }

    /// <summary>
    /// Batch insert using Dapper's native collection support.
    /// Single roundtrip for all rows - efficient for most seed data.
    /// </summary>
    protected async Task<int> BatchInsertAsync<T>(
        SqlConnection connection,
        SqlTransaction transaction,
        string insertSql,
        IEnumerable<T> items)
    {
        return await connection.ExecuteAsync(insertSql, items, transaction);
    }

    /// <summary>
    /// High-performance bulk insert using SqlBulkCopy.
    /// Use for very large datasets (10k+ rows) - significantly faster than Dapper batch.
    /// </summary>
    protected async Task BulkCopyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tableName,
        DataTable dataTable,
        int batchSize = 5000)
    {
        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
        {
            DestinationTableName = tableName,
            BatchSize = batchSize
        };

        // Map columns by name
        foreach (DataColumn column in dataTable.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(dataTable);
    }

    /// <summary>
    /// Executes a single INSERT and returns the affected row count.
    /// </summary>
    protected async Task<int> InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string insertSql,
        object parameters)
    {
        return await connection.ExecuteAsync(insertSql, parameters, transaction);
    }

    /// <summary>
    /// Executes a query and returns results.
    /// </summary>
    protected async Task<IEnumerable<T>> QueryAsync<T>(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        object? parameters = null)
    {
        return await connection.QueryAsync<T>(sql, parameters, transaction);
    }

    /// <summary>
    /// Executes a query and returns a single result.
    /// </summary>
    protected async Task<T?> QueryFirstOrDefaultAsync<T>(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        object? parameters = null)
    {
        return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters, transaction);
    }

    /// <summary>
    /// Helper to log seed completion with timing.
    /// </summary>
    protected void LogSeedComplete(string entityName, int created, int skipped, TimeSpan duration)
    {
        _logger.LogInformation(
            "{EntityName} seed complete: Created {Created}, Skipped {Skipped} in {Duration:0.00}ms",
            entityName, created, skipped, duration.TotalMilliseconds);
    }
}
