namespace IdentityCenter.API.Services;

/// <summary>
/// Read-only access to the rolling Serilog log files for the /admin/logs page, HARD-LOCKED to
/// the configured log directory:
///   - callers never supply a path — only a file NAME chosen from <see cref="ListLogFiles"/>;
///   - names are validated against the expected "identitycenter-api-*.log" pattern, rejected if
///     they contain separators/traversal, and the resolved full path is verified to still live
///     inside the log directory (defense in depth against path traversal);
///   - reads are tail-bounded so a multi-hundred-MB file cannot be pulled into memory.
/// </summary>
public sealed class LogFileService
{
    private const string FilePrefix = "identitycenter-api-";
    private const string FileExtension = ".log";
    private const long MaxTailBytes = 2 * 1024 * 1024; // read at most the last 2 MB of a file

    private readonly string _logDirectory;

    public LogFileService(IConfiguration configuration)
    {
        // Same resolution as the Serilog file sink bootstrap in Program.cs.
        var configured = configuration["Logging:Directory"];
        _logDirectory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "IdentityCenter", "logs")
            : configured;
    }

    public string LogDirectory => _logDirectory;

    /// <summary>Log file names (no paths) in the log directory, newest first.</summary>
    public IReadOnlyList<LogFileInfo> ListLogFiles()
    {
        try
        {
            if (!Directory.Exists(_logDirectory)) return Array.Empty<LogFileInfo>();

            return Directory.EnumerateFiles(_logDirectory, FilePrefix + "*" + FileExtension)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new LogFileInfo(f.Name, f.Length, f.LastWriteTimeUtc))
                .ToList();
        }
        catch
        {
            return Array.Empty<LogFileInfo>();
        }
    }

    /// <summary>
    /// Returns up to <paramref name="maxLines"/> lines from the END of the named log file.
    /// Returns null if the name fails validation or the file does not exist.
    /// </summary>
    public async Task<IReadOnlyList<string>?> ReadTailAsync(string fileName, int maxLines = 500)
    {
        var fullPath = ResolveAndValidate(fileName);
        if (fullPath is null) return null;

        try
        {
            // shared:true sink keeps the file open — read with full sharing.
            using var stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length > MaxTailBytes)
                stream.Seek(-MaxTailBytes, SeekOrigin.End);

            using var reader = new StreamReader(stream);
            var lines = new LinkedList<string>();
            while (await reader.ReadLineAsync() is { } line)
            {
                lines.AddLast(line);
                if (lines.Count > maxLines) lines.RemoveFirst();
            }
            return lines.ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Validates a caller-supplied file NAME and resolves it inside the log directory.
    /// Returns null on any irregularity.
    /// </summary>
    private string? ResolveAndValidate(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // Names only — any path separator or traversal token is an immediate reject.
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..")) return null;
        if (!fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (!fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) return null;

        var fullPath = Path.GetFullPath(Path.Combine(_logDirectory, fileName));

        // Belt and braces: the resolved path must still be inside the log directory.
        var dirWithSep = Path.GetFullPath(_logDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(dirWithSep, StringComparison.OrdinalIgnoreCase)) return null;

        return File.Exists(fullPath) ? fullPath : null;
    }
}

public sealed record LogFileInfo(string Name, long SizeBytes, DateTime LastWriteUtc);
