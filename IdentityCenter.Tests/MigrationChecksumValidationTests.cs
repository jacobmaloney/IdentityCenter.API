using System.Security.Cryptography;
using System.Text;
using DataAccessLibrary.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IdentityCenter.Tests;

/// <summary>
/// Proves the migration-runner schema-drift guard: when an already-applied migration's stored
/// checksum no longer matches the embedded script for that version, the runner detects it and
/// fails closed by default (throw + halt) — or, with the config escape hatch off, downgrades to a
/// warning. Hermetic: drives the pure classification + policy seams of DatabaseMigrationService
/// directly, so no database is required.
/// </summary>
public class MigrationChecksumValidationTests
{
    private const string DriftVersionFile = "V007__Tamper_Test.sql";

    // Embedded scripts as they exist in source *now* (post-edit).
    private static List<MigrationScript> EmbeddedScripts() => new()
    {
        Script(7, DriftVersionFile, "CREATE TABLE Tamper (Id INT); -- edited since it was applied"),
        Script(8, "V008__Clean.sql", "CREATE TABLE Clean (Id INT);"),
        Script(9, "V009__PreChecksum.sql", "CREATE TABLE Pre (Id INT);"),
    };

    // __SchemaVersion rows as they were recorded when each migration was applied.
    private static List<AppliedMigration> AppliedRows() => new()
    {
        // V007 was applied from DIFFERENT content than the embedded script now holds -> drift.
        Applied(7, DriftVersionFile, Sha("CREATE TABLE Tamper (Id INT); -- original content when applied")),
        // V008 stored checksum matches its current embedded content -> clean.
        Applied(8, "V008__Clean.sql", Sha("CREATE TABLE Clean (Id INT);")),
        // V009 predates the Checksum column -> nothing to compare, must be skipped.
        Applied(9, "V009__PreChecksum.sql", null),
        // V006 is in the DB but no longer has an embedded script -> unknown applied migration.
        Applied(6, "V006__Removed.sql", Sha("CREATE TABLE Removed (Id INT);")),
    };

    [Fact]
    public void FailOnMismatchTrue_Throws_AndMessageNamesVersionAndFile()
    {
        var findings = DatabaseMigrationService.EvaluateAppliedChecksums(AppliedRows(), EmbeddedScripts());

        var ex = Assert.Throws<InvalidOperationException>(
            () => DatabaseMigrationService.EnforceFailClosedPolicy(findings, failOnMismatch: true));

        Assert.Contains("V7", ex.Message);
        Assert.Contains(DriftVersionFile, ex.Message);
        Assert.Contains("schema drift", ex.Message);
    }

    [Fact]
    public void FailOnMismatchFalse_DoesNotThrow_AndDriftIsSurfacedAsWarning()
    {
        var findings = DatabaseMigrationService.EvaluateAppliedChecksums(AppliedRows(), EmbeddedScripts());

        // Escape hatch on: the runner must NOT halt.
        var ex = Record.Exception(
            () => DatabaseMigrationService.EnforceFailClosedPolicy(findings, failOnMismatch: false));
        Assert.Null(ex);

        // ...but the drift is still classified so the caller logs it as a warning.
        var drift = Assert.Single(findings, f => f.Kind == ChecksumFindingKind.Drift);
        Assert.Contains(DriftVersionFile, drift.Message);
    }

    [Fact]
    public void Classification_SkipsNullChecksum_MatchesClean_AndFlagsUnknownApplied()
    {
        var findings = DatabaseMigrationService.EvaluateAppliedChecksums(AppliedRows(), EmbeddedScripts());

        // Exactly two findings: the V007 drift and the V006 unknown-applied. V008 (match) and
        // V009 (null checksum) produce nothing.
        Assert.Equal(2, findings.Count);
        Assert.Single(findings, f => f.Kind == ChecksumFindingKind.Drift && f.Version == 7);

        var unknown = Assert.Single(findings, f => f.Kind == ChecksumFindingKind.UnknownApplied);
        Assert.Equal(6, unknown.Version);

        // An unknown applied migration alone must NOT halt startup, even fail-closed.
        var ex = Record.Exception(
            () => DatabaseMigrationService.EnforceFailClosedPolicy(
                findings.Where(f => f.Kind == ChecksumFindingKind.UnknownApplied), failOnMismatch: true));
        Assert.Null(ex);
    }

    [Fact]
    public void ConstructorDefault_IsFailClosed_WhenConfigKeyIsAbsent()
    {
        // FORK TRAP PIN: with Migrations:ChecksumValidation:FailOnMismatch UNSET, the runner must
        // default to fail-CLOSED (true). A mirrored constructor that defaults false ships a silent
        // fail-open drift guard — this test makes that regression loud.
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=test;Database=IdentityCenter;"
            }).Build();

        var service = new DatabaseMigrationService(
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseMigrationService>.Instance);

        var field = typeof(DatabaseMigrationService).GetField(
            "_failOnChecksumMismatch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(true, field!.GetValue(service));
    }

    private static MigrationScript Script(int version, string name, string content) => new()
    {
        Version = version,
        ScriptName = name,
        Content = content,
        Checksum = Sha(content)
    };

    private static AppliedMigration Applied(int version, string name, string? checksum) => new()
    {
        Version = version,
        ScriptName = name,
        Checksum = checksum
    };

    private static string Sha(string content)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(content)));
    }
}
