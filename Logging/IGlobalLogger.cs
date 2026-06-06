using System;
using Microsoft.Extensions.Logging;

namespace Logging
{
    public interface IGlobalLogger
    {
        // Standard log levels
        void LogTrace(string message, params object[] args);
        void LogDebug(string message, params object[] args);
        void LogInformation(string message, params object[] args);
        void LogWarning(string message, params object[] args);
        void LogError(Exception ex, string message, params object[] args);
        void LogCritical(Exception ex, string message, params object[] args);

        // Overloads for logging without exceptions
        void LogWarning(Exception ex, string message, params object[] args);
        void LogError(string message, params object[] args);

        // Security and audit logging
        void LogSecurity(string action, string userId, string details);
        void LogAudit(string entity, string action, string userId, object changes);

        // Log level management
        void SetLogLevel(string category, LogLevel level);
        LogLevel GetLogLevel(string category);

        // Helper methods for common patterns
        void LogMethodEntry(string methodName, params object[] parameters);
        void LogMethodExit(string methodName);
        void LogMethodError(string methodName, Exception ex);

        // Operation scope helpers
        IDisposable BeginOperationScope(string operationName, object parameters);
        void LogOperationSuccess(string operationName, object result);
        void LogOperationFailure(string operationName, Exception ex);
    }
}