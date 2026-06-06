using DataAccessLibrary.ControlPlane;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Base class for all Dapper-based repositories.
/// Provides connection management, error handling, and structured logging.
/// EF Core is ONLY for migrations - Dapper for ALL queries.
///
/// MULTI-TENANT SEAM (SaaS Day 4): the connection string is no longer captured once at construction.
/// It is resolved PER CALL via <see cref="_connectionString"/>, which consults the ambient
/// <see cref="TenantConnectionAccessor"/>:
///   - When a per-request tenant resolver is installed (the multi-tenant API), this returns the CURRENT
///     request's tenant DB (or DefaultConnection for an admin/unresolved request). Every one of the 300+
///     existing <c>_connectionString</c> references and both helper methods below become tenant-aware for
///     free, with no change to the 40+ derived repositories.
///   - When NO resolver is installed (WebPortal single-tenant, or the API before/outside a request),
///     this returns DefaultConnection — byte-for-byte the previous behavior. This is the backward-compat
///     guarantee: absence of a resolver == legacy single-tenant path, untouched.
/// </summary>
public abstract class DapperRepositoryBase
{
    private readonly string _defaultConnectionString;
    protected readonly IGlobalLogger _logger;

    protected DapperRepositoryBase(IConfiguration configuration, IGlobalLogger logger)
    {
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The connection string to use for THIS call. Resolved per access (not cached) so it always
    /// reflects the current request's tenant. Falls back to DefaultConnection when no tenant resolver is
    /// installed for the current async flow (legacy single-tenant path). Read identically by all derived
    /// repositories that reference <c>_connectionString</c> directly.
    /// </summary>
    protected string _connectionString =>
        TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

    /// <summary>
    /// Executes a database operation with connection management, logging, and error handling.
    /// Uses [CallerMemberName] for automatic method name in logs.
    /// </summary>
    protected async Task<T> ExecuteAsync<T>(
        Func<SqlConnection, Task<T>> operation,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string? callerName = null)
    {
        var methodName = callerName ?? "Unknown";
        _logger.LogMethodEntry(methodName);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await operation(connection).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(methodName, ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(methodName);
        }
    }

    /// <summary>
    /// Executes a void database operation with connection management, logging, and error handling.
    /// Uses [CallerMemberName] for automatic method name in logs.
    /// </summary>
    protected async Task ExecuteNonQueryAsync(
        Func<SqlConnection, Task> operation,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string? callerName = null)
    {
        var methodName = callerName ?? "Unknown";
        _logger.LogMethodEntry(methodName);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await operation(connection).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(methodName, ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(methodName);
        }
    }
}
