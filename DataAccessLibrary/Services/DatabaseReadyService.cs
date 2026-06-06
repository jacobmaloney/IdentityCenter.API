using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Dapper;

namespace DataAccessLibrary.Services;

/// <summary>
/// Centralized service to check if the database schema is ready.
/// All background services and jobs should check this before querying the database.
/// This prevents error spam during initial setup when migrations haven't been applied yet.
/// </summary>
public interface IDatabaseReadyService
{
    /// <summary>
    /// Returns true if the database schema is ready (migrations applied, tables exist).
    /// Safe to call frequently - results are cached for 30 seconds.
    /// </summary>
    Task<bool> IsDatabaseReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the ready state, forcing the next check to re-verify the schema.
    /// Call this if you detect a schema change.
    /// </summary>
    void ResetReadyState();
}

public class DatabaseReadyService : IDatabaseReadyService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseReadyService> _logger;

    private bool _isReady = false;
    private DateTime _lastCheck = DateTime.MinValue;
    private readonly TimeSpan _cacheTimeout = TimeSpan.FromSeconds(30);
    private readonly object _lock = new();

    public DatabaseReadyService(IConfiguration configuration, ILogger<DatabaseReadyService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    public async Task<bool> IsDatabaseReadyAsync(CancellationToken cancellationToken = default)
    {
        // Fast path - already verified as ready
        lock (_lock)
        {
            if (_isReady)
                return true;

            // Check if we're within the cache timeout
            if ((DateTime.UtcNow - _lastCheck) < _cacheTimeout)
                return false;
        }

        // Slow path - need to verify schema
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Check for pending migrations by querying the __EFMigrationsHistory table
            // and comparing with expected migrations count
            var appliedMigrations = await connection.QueryAsync<string>(
                new CommandDefinition(
                    "SELECT MigrationId FROM __EFMigrationsHistory",
                    cancellationToken: cancellationToken));

            // Note: Without EF Core, we can't easily determine pending migrations.
            // Instead, we just verify the migrations table exists and has entries,
            // then check if core tables exist.

            // Verify a core table exists
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT TOP 1 1 FROM AspNetUsers",
                    cancellationToken: cancellationToken));

            lock (_lock)
            {
                _isReady = true;
                _lastCheck = DateTime.UtcNow;
            }

            _logger.LogInformation("DatabaseReadyService: Database schema is ready");
            return true;
        }
        catch (SqlException sqlEx) when (sqlEx.Number == 208) // Invalid object name
        {
            _logger.LogDebug("DatabaseReadyService: Schema not ready (table doesn't exist)");
            lock (_lock) { _lastCheck = DateTime.UtcNow; }
            return false;
        }
        catch (SqlException sqlEx) when (sqlEx.Number == 4060) // Cannot open database
        {
            _logger.LogDebug("DatabaseReadyService: Database doesn't exist yet");
            lock (_lock) { _lastCheck = DateTime.UtcNow; }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DatabaseReadyService: Schema check failed");
            lock (_lock) { _lastCheck = DateTime.UtcNow; }
            return false;
        }
    }

    public void ResetReadyState()
    {
        lock (_lock)
        {
            _isReady = false;
            _lastCheck = DateTime.MinValue;
        }
        _logger.LogDebug("DatabaseReadyService: Ready state reset");
    }
}
