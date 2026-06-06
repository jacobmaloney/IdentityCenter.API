using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Logging
{
    public class GlobalLogger : IGlobalLogger
    {
        private readonly ILogger<GlobalLogger> _logger;
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private static readonly Dictionary<string, LogLevel> _logLevels = new();

        public GlobalLogger(
            ILogger<GlobalLogger> logger,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        // Standard log levels
        public void LogTrace(string message, params object[] args)
        {
            _logger.LogTrace(message, args);
        }

        public void LogDebug(string message, params object[] args)
        {
            _logger.LogDebug(message, args);
        }

        public void LogInformation(string message, params object[] args)
        {
            _logger.LogInformation(message, args);
        }

        public void LogWarning(string message, params object[] args)
        {
            _logger.LogWarning(message, args);
        }

        public void LogWarning(Exception ex, string message, params object[] args)
        {
            _logger.LogWarning(ex, message, args);
        }

        public void LogError(Exception ex, string message, params object[] args)
        {
            _logger.LogError(ex, message, args);
        }

        public void LogError(string message, params object[] args)
        {
            _logger.LogError(message, args);
        }

        public void LogCritical(Exception ex, string message, params object[] args)
        {
            _logger.LogCritical(ex, message, args);
        }

        // Security and audit logging
        public void LogSecurity(string action, string userId, string details)
        {
            var ipAddress = _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var userAgent = _httpContextAccessor?.HttpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown";

            _logger.LogWarning("SECURITY: {Action} by {UserId} from {IpAddress}. Details: {Details}",
                action, userId, ipAddress, details);

            // Note: Database persistence can be added via IAuditRepository
            // Direct injection here creates a circular dependency
            // Solution: Create a separate AuditService that handles database persistence
        }

        public void LogAudit(string entity, string action, string userId, object changes)
        {
            var ipAddress = _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var changesJson = JsonSerializer.Serialize(changes, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            _logger.LogInformation("AUDIT: {Entity} {Action} by {UserId}. Changes: {Changes}",
                entity, action, userId, changesJson);

            // Note: Database persistence will be added via AuditService
        }

        // Log level management
        public void SetLogLevel(string category, LogLevel level)
        {
            _logLevels[category] = level;
            _logger.LogInformation("Log level for category '{Category}' set to {Level}", category, level);
        }

        public LogLevel GetLogLevel(string category)
        {
            return _logLevels.TryGetValue(category, out var level)
                ? level
                : LogLevel.Information;
        }

        // Helper methods for common patterns
        public void LogMethodEntry(string methodName, params object[] parameters)
        {
            if (parameters.Length > 0)
            {
                _logger.LogTrace("→ Entering {MethodName} with parameters: {@Parameters}", methodName, parameters);
            }
            else
            {
                _logger.LogTrace("→ Entering {MethodName}", methodName);
            }
        }

        public void LogMethodExit(string methodName)
        {
            _logger.LogTrace("← Exiting {MethodName}", methodName);
        }

        public void LogMethodError(string methodName, Exception ex)
        {
            _logger.LogError(ex, "✗ Error in {MethodName}: {ErrorMessage}", methodName, ex.Message);
        }

        // Operation scope helpers
        public IDisposable BeginOperationScope(string operationName, object parameters)
        {
            var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = operationName,
                ["Parameters"] = parameters
            });

            _logger.LogDebug("⚙ Starting operation: {Operation} with {@Parameters}", operationName, parameters);
            return new OperationScope(_logger, operationName, scope);
        }

        public void LogOperationSuccess(string operationName, object result)
        {
            _logger.LogInformation("✓ Operation completed successfully: {Operation} - Result: {@Result}",
                operationName, result);
        }

        public void LogOperationFailure(string operationName, Exception ex)
        {
            _logger.LogError(ex, "✗ Operation failed: {Operation} - Error: {ErrorMessage}",
                operationName, ex.Message);
        }

        // Private helper class for operation scopes
        private class OperationScope : IDisposable
        {
            private readonly ILogger _logger;
            private readonly string _operationName;
            private readonly IDisposable _scope;
            private readonly System.Diagnostics.Stopwatch _stopwatch;

            public OperationScope(ILogger logger, string operationName, IDisposable scope)
            {
                _logger = logger;
                _operationName = operationName;
                _scope = scope;
                _stopwatch = System.Diagnostics.Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _stopwatch.Stop();
                _logger.LogDebug("⚙ Completed operation: {Operation} in {ElapsedMs}ms",
                    _operationName, _stopwatch.ElapsedMilliseconds);
                _scope?.Dispose();
            }
        }
    }
}
