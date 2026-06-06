using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services.Scripting;

/// <summary>
/// Logger interface for scripts.
/// Provides simple logging methods that scripts can call.
/// Logs are collected and stored in SyncScriptExecution.OutputLog as JSON.
/// </summary>
public interface IScriptLogger
{
    /// <summary>
    /// Log a debug message (only visible when verbose logging is enabled).
    /// </summary>
    void Debug(string message);

    /// <summary>
    /// Log an informational message.
    /// </summary>
    void Info(string message);

    /// <summary>
    /// Log a warning message.
    /// </summary>
    void Warning(string message);

    /// <summary>
    /// Log an error message.
    /// </summary>
    void Error(string message);

    /// <summary>
    /// Log an error with exception details.
    /// </summary>
    void Error(string message, Exception exception);

    /// <summary>
    /// Get all log entries collected during script execution.
    /// </summary>
    List<ScriptLogEntry> GetLogEntries();

    /// <summary>
    /// Get log entries as JSON for storage.
    /// </summary>
    string GetLogEntriesJson();

    /// <summary>
    /// Clear all log entries.
    /// </summary>
    void Clear();
}

/// <summary>
/// Default implementation of IScriptLogger.
/// Collects log entries and optionally forwards them to an ILogger.
/// </summary>
public class ScriptLogger : IScriptLogger
{
    private readonly List<ScriptLogEntry> _entries = new();
    private readonly ILogger? _forwardTo;
    private readonly string _scriptName;
    private readonly bool _enableDebug;
    private readonly object _lock = new();

    /// <summary>
    /// Create a new ScriptLogger.
    /// </summary>
    /// <param name="scriptName">Name of the script (for log context)</param>
    /// <param name="forwardTo">Optional ILogger to forward logs to</param>
    /// <param name="enableDebug">Whether to collect debug-level logs</param>
    public ScriptLogger(string scriptName, ILogger? forwardTo = null, bool enableDebug = false)
    {
        _scriptName = scriptName;
        _forwardTo = forwardTo;
        _enableDebug = enableDebug;
    }

    public void Debug(string message)
    {
        if (!_enableDebug) return;

        lock (_lock)
        {
            _entries.Add(new ScriptLogEntry(ScriptLogLevel.Debug, message));
        }

        _forwardTo?.LogDebug("[Script:{ScriptName}] {Message}", _scriptName, message);
    }

    public void Info(string message)
    {
        lock (_lock)
        {
            _entries.Add(new ScriptLogEntry(ScriptLogLevel.Info, message));
        }

        _forwardTo?.LogInformation("[Script:{ScriptName}] {Message}", _scriptName, message);
    }

    public void Warning(string message)
    {
        lock (_lock)
        {
            _entries.Add(new ScriptLogEntry(ScriptLogLevel.Warning, message));
        }

        _forwardTo?.LogWarning("[Script:{ScriptName}] {Message}", _scriptName, message);
    }

    public void Error(string message)
    {
        lock (_lock)
        {
            _entries.Add(new ScriptLogEntry(ScriptLogLevel.Error, message));
        }

        _forwardTo?.LogError("[Script:{ScriptName}] {Message}", _scriptName, message);
    }

    public void Error(string message, Exception exception)
    {
        var fullMessage = $"{message}: {exception.Message}";

        lock (_lock)
        {
            _entries.Add(new ScriptLogEntry(ScriptLogLevel.Error, fullMessage));
        }

        _forwardTo?.LogError(exception, "[Script:{ScriptName}] {Message}", _scriptName, message);
    }

    public List<ScriptLogEntry> GetLogEntries()
    {
        lock (_lock)
        {
            return new List<ScriptLogEntry>(_entries);
        }
    }

    public string GetLogEntriesJson()
    {
        lock (_lock)
        {
            if (_entries.Count == 0) return "[]";

            var entries = _entries.Select(e => new
            {
                t = e.Timestamp.ToString("O"),
                l = e.Level.ToString()[0], // D, I, W, E
                m = e.Message
            });

            return System.Text.Json.JsonSerializer.Serialize(entries);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}

/// <summary>
/// Factory for creating script loggers.
/// </summary>
public interface IScriptLoggerFactory
{
    /// <summary>
    /// Create a logger for a specific script.
    /// </summary>
    IScriptLogger CreateLogger(string scriptName, bool enableDebug = false);
}

/// <summary>
/// Default implementation of IScriptLoggerFactory.
/// </summary>
public class ScriptLoggerFactory : IScriptLoggerFactory
{
    private readonly ILoggerFactory? _loggerFactory;

    public ScriptLoggerFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
    }

    public IScriptLogger CreateLogger(string scriptName, bool enableDebug = false)
    {
        var logger = _loggerFactory?.CreateLogger($"Script.{scriptName}");
        return new ScriptLogger(scriptName, logger, enableDebug);
    }
}
