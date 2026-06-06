using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services.Scripting;

/// <summary>
/// Service for compiling and executing C# scripts using Roslyn.
/// Provides caching of compiled scripts for performance.
/// </summary>
public interface IScriptCompilationService
{
    /// <summary>
    /// Compile a script and cache the result.
    /// </summary>
    Task<ScriptCompilationResult> CompileScriptAsync(SyncProcessingScript script, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a pre-processing script.
    /// </summary>
    Task<ScriptExecutionResult> ExecutePreProcessingScriptAsync(
        SyncProcessingScript script,
        PreProcessingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a post-processing script.
    /// </summary>
    Task<ScriptExecutionResult> ExecutePostProcessingScriptAsync(
        SyncProcessingScript script,
        PostProcessingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidate cached compiled script (call when script is modified).
    /// </summary>
    void InvalidateCache(Guid scriptId);

    /// <summary>
    /// Clear all cached compiled scripts.
    /// </summary>
    void ClearCache();
}

/// <summary>
/// Result of script compilation.
/// </summary>
public class ScriptCompilationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ErrorLine { get; set; }
    public int? ErrorColumn { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Roslyn-based implementation of IScriptCompilationService.
/// </summary>
public class ScriptCompilationService : IScriptCompilationService
{
    private readonly ILogger<ScriptCompilationService> _logger;
    private readonly IScriptLoggerFactory _scriptLoggerFactory;

    // Cache of compiled pre-processing scripts
    private readonly ConcurrentDictionary<Guid, CachedScript<PreProcessingGlobals>> _preProcessingCache = new();

    // Cache of compiled post-processing scripts
    private readonly ConcurrentDictionary<Guid, CachedScript<PostProcessingGlobals>> _postProcessingCache = new();

    // Script execution timeout (prevent infinite loops)
    private readonly TimeSpan _executionTimeout = TimeSpan.FromMinutes(5);

    // Standard imports for all scripts
    private static readonly string[] StandardImports = new[]
    {
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Text.RegularExpressions",
        "DataAccessLibrary.Models",
        "DataAccessLibrary.Repositories",
        "DataAccessLibrary.Services.Scripting"
    };

    // Assemblies to reference
    private static readonly Assembly[] ReferencedAssemblies = new[]
    {
        typeof(object).Assembly,                    // mscorlib/System.Private.CoreLib
        typeof(Enumerable).Assembly,                // System.Linq
        typeof(List<>).Assembly,                    // System.Collections
        typeof(Guid).Assembly,                      // System.Runtime
        typeof(System.Security.Principal.SecurityIdentifier).Assembly, // For objectSid conversion
        typeof(IdentityObject).Assembly,            // DataAccessLibrary models
        typeof(ISyncRepository).Assembly,           // DataAccessLibrary repositories
    };

    public ScriptCompilationService(
        ILogger<ScriptCompilationService> logger,
        IScriptLoggerFactory scriptLoggerFactory)
    {
        _logger = logger;
        _scriptLoggerFactory = scriptLoggerFactory;
    }

    public async Task<ScriptCompilationResult> CompileScriptAsync(
        SyncProcessingScript script,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Compiling script: {ScriptName} (Type: {ScriptType})", script.Name, script.ScriptType);

            var options = ScriptOptions.Default
                .WithImports(StandardImports)
                .WithReferences(ReferencedAssemblies)
                .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release);

            if (script.ScriptType == ScriptTypes.PreProcessing)
            {
                var compiledScript = CSharpScript.Create<object>(
                    script.ScriptCode,
                    options,
                    typeof(PreProcessingGlobals));

                var diagnostics = compiledScript.Compile(cancellationToken);
                var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
                var warnings = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                    .Select(d => d.GetMessage())
                    .ToList();

                if (errors.Any())
                {
                    var firstError = errors.First();
                    var lineSpan = firstError.Location.GetLineSpan();

                    _logger.LogWarning("Script compilation failed: {ScriptName} - {Error}",
                        script.Name, firstError.GetMessage());

                    return new ScriptCompilationResult
                    {
                        Success = false,
                        ErrorMessage = firstError.GetMessage(),
                        ErrorLine = lineSpan.StartLinePosition.Line + 1,
                        ErrorColumn = lineSpan.StartLinePosition.Character + 1,
                        Warnings = warnings
                    };
                }

                // Cache the compiled script
                _preProcessingCache[script.Id] = new CachedScript<PreProcessingGlobals>
                {
                    Script = compiledScript,
                    Version = script.Version,
                    CompiledAt = DateTime.UtcNow
                };

                _logger.LogInformation("Script compiled successfully: {ScriptName}", script.Name);
                return new ScriptCompilationResult { Success = true, Warnings = warnings };
            }
            else // PostProcessing
            {
                var compiledScript = CSharpScript.Create<object>(
                    script.ScriptCode,
                    options,
                    typeof(PostProcessingGlobals));

                var diagnostics = compiledScript.Compile(cancellationToken);
                var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
                var warnings = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                    .Select(d => d.GetMessage())
                    .ToList();

                if (errors.Any())
                {
                    var firstError = errors.First();
                    var lineSpan = firstError.Location.GetLineSpan();

                    _logger.LogWarning("Script compilation failed: {ScriptName} - {Error}",
                        script.Name, firstError.GetMessage());

                    return new ScriptCompilationResult
                    {
                        Success = false,
                        ErrorMessage = firstError.GetMessage(),
                        ErrorLine = lineSpan.StartLinePosition.Line + 1,
                        ErrorColumn = lineSpan.StartLinePosition.Character + 1,
                        Warnings = warnings
                    };
                }

                // Cache the compiled script
                _postProcessingCache[script.Id] = new CachedScript<PostProcessingGlobals>
                {
                    Script = compiledScript,
                    Version = script.Version,
                    CompiledAt = DateTime.UtcNow
                };

                _logger.LogInformation("Script compiled successfully: {ScriptName}", script.Name);
                return new ScriptCompilationResult { Success = true, Warnings = warnings };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compiling script: {ScriptName}", script.Name);
            return new ScriptCompilationResult
            {
                Success = false,
                ErrorMessage = $"Compilation error: {ex.Message}"
            };
        }
    }

    public async Task<ScriptExecutionResult> ExecutePreProcessingScriptAsync(
        SyncProcessingScript script,
        PreProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = _scriptLoggerFactory.CreateLogger(script.Name, enableDebug: true);

        try
        {
            _logger.LogDebug("Executing pre-processing script: {ScriptName}", script.Name);

            // Get or compile the script
            if (!_preProcessingCache.TryGetValue(script.Id, out var cached) || cached.Version != script.Version)
            {
                var compileResult = await CompileScriptAsync(script, cancellationToken);
                if (!compileResult.Success)
                {
                    return ScriptExecutionResult.Failed(
                        compileResult.ErrorMessage ?? "Compilation failed",
                        null,
                        (int)stopwatch.ElapsedMilliseconds,
                        logger.GetLogEntries());
                }

                cached = _preProcessingCache[script.Id];
            }

            // Create globals for the script
            var globals = new PreProcessingGlobals
            {
                SourceObjects = context.SourceObjects,
                Step = context.Step,
                Project = context.Project,
                Log = logger,
                Repository = context.Repository,
                CancellationToken = cancellationToken
            };

            // Execute with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_executionTimeout);

            try
            {
                await cached.Script.RunAsync(globals, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return ScriptExecutionResult.Failed(
                    $"Script execution timed out after {_executionTimeout.TotalMinutes} minutes",
                    null,
                    (int)stopwatch.ElapsedMilliseconds,
                    logger.GetLogEntries());
            }

            stopwatch.Stop();
            _logger.LogInformation("Pre-processing script completed: {ScriptName} in {Duration}ms",
                script.Name, stopwatch.ElapsedMilliseconds);

            return ScriptExecutionResult.Succeeded(
                (int)stopwatch.ElapsedMilliseconds,
                new ScriptMetrics(),
                logger.GetLogEntries(),
                context.SourceObjects.Count);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error executing pre-processing script: {ScriptName}", script.Name);

            return ScriptExecutionResult.Failed(
                ex.Message,
                ex.StackTrace,
                (int)stopwatch.ElapsedMilliseconds,
                logger.GetLogEntries());
        }
    }

    public async Task<ScriptExecutionResult> ExecutePostProcessingScriptAsync(
        SyncProcessingScript script,
        PostProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = _scriptLoggerFactory.CreateLogger(script.Name, enableDebug: true);
        var metrics = new ScriptMetrics();

        try
        {
            _logger.LogDebug("Executing post-processing script: {ScriptName}", script.Name);

            // Get or compile the script
            if (!_postProcessingCache.TryGetValue(script.Id, out var cached) || cached.Version != script.Version)
            {
                var compileResult = await CompileScriptAsync(script, cancellationToken);
                if (!compileResult.Success)
                {
                    return ScriptExecutionResult.Failed(
                        compileResult.ErrorMessage ?? "Compilation failed",
                        null,
                        (int)stopwatch.ElapsedMilliseconds,
                        logger.GetLogEntries());
                }

                cached = _postProcessingCache[script.Id];
            }

            // Create globals for the script
            var globals = new PostProcessingGlobals
            {
                SyncedObjects = context.SyncedObjects,
                ObjectAttributes = context.ObjectAttributes,
                Step = context.Step,
                Log = logger,
                Repository = context.Repository,
                Metrics = metrics,
                CancellationToken = cancellationToken
            };

            // Execute with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_executionTimeout);

            try
            {
                await cached.Script.RunAsync(globals, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return ScriptExecutionResult.Failed(
                    $"Script execution timed out after {_executionTimeout.TotalMinutes} minutes",
                    null,
                    (int)stopwatch.ElapsedMilliseconds,
                    logger.GetLogEntries());
            }

            stopwatch.Stop();
            _logger.LogInformation("Post-processing script completed: {ScriptName} in {Duration}ms - {Summary}",
                script.Name, stopwatch.ElapsedMilliseconds, metrics.GetSummary());

            return ScriptExecutionResult.Succeeded(
                (int)stopwatch.ElapsedMilliseconds,
                metrics,
                logger.GetLogEntries(),
                context.SyncedObjects.Count);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error executing post-processing script: {ScriptName}", script.Name);

            return ScriptExecutionResult.Failed(
                ex.Message,
                ex.StackTrace,
                (int)stopwatch.ElapsedMilliseconds,
                logger.GetLogEntries());
        }
    }

    public void InvalidateCache(Guid scriptId)
    {
        _preProcessingCache.TryRemove(scriptId, out _);
        _postProcessingCache.TryRemove(scriptId, out _);
        _logger.LogDebug("Invalidated cache for script: {ScriptId}", scriptId);
    }

    public void ClearCache()
    {
        _preProcessingCache.Clear();
        _postProcessingCache.Clear();
        _logger.LogInformation("Cleared all script cache");
    }

    private class CachedScript<TGlobals>
    {
        public Script<object> Script { get; set; } = null!;
        public int Version { get; set; }
        public DateTime CompiledAt { get; set; }
    }
}

/// <summary>
/// Global variables available to pre-processing scripts.
/// Scripts access these directly as global variables.
/// </summary>
public class PreProcessingGlobals
{
    /// <summary>
    /// The source objects to process. Scripts can modify this collection.
    /// </summary>
    public List<Dictionary<string, object>> SourceObjects { get; set; } = new();

    /// <summary>
    /// The sync step being executed.
    /// </summary>
    public SyncStep Step { get; set; } = null!;

    /// <summary>
    /// The parent sync project.
    /// </summary>
    public SyncProject Project { get; set; } = null!;

    /// <summary>
    /// Logger for script output.
    /// </summary>
    public IScriptLogger Log { get; set; } = null!;

    /// <summary>
    /// Repository for database operations.
    /// </summary>
    public ISyncRepository Repository { get; set; } = null!;

    /// <summary>
    /// Cancellation token for graceful shutdown.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// Global variables available to post-processing scripts.
/// Scripts access these directly as global variables.
/// </summary>
public class PostProcessingGlobals
{
    /// <summary>
    /// The synced objects from the database.
    /// </summary>
    public List<IdentityObject> SyncedObjects { get; set; } = new();

    /// <summary>
    /// Extended attributes for each object.
    /// </summary>
    public Dictionary<Guid, List<ObjectAttribute>> ObjectAttributes { get; set; } = new();

    /// <summary>
    /// The sync step being executed.
    /// </summary>
    public SyncStep Step { get; set; } = null!;

    /// <summary>
    /// Logger for script output.
    /// </summary>
    public IScriptLogger Log { get; set; } = null!;

    /// <summary>
    /// Repository for database operations.
    /// </summary>
    public ISyncRepository Repository { get; set; } = null!;

    /// <summary>
    /// Metrics tracker for the script execution.
    /// </summary>
    public ScriptMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Cancellation token for graceful shutdown.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}
