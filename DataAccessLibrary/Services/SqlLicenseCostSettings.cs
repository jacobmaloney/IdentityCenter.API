using System.Data;
using System.Globalization;
using Dapper;

namespace DataAccessLibrary.Services;

/// <summary>
/// Single source of truth for SQL Server per-core pricing. Values live in
/// Settings(Category='SqlLicense') under EnterpriseCostPerCoreAnnual,
/// StandardCostPerCoreAnnual and BlendedCostPerCore, with the historical
/// hardcoded literals as fallback defaults. No editable UI yet — upsert the
/// Settings rows (IConfigurationRepository.UpsertSettingAsync) to override.
/// </summary>
public static class SqlLicenseCostSettings
{
    public const string Category = "SqlLicense";
    public const string EnterpriseKey = "EnterpriseCostPerCoreAnnual";
    public const string StandardKey = "StandardCostPerCoreAnnual";
    public const string BlendedKey = "BlendedCostPerCore";

    // SQL Server 2022 list price is quoted per 2-CORE PACK: Enterprise $15,123, Standard $3,945.
    // These fields are PER CORE and the calculator charges (RequiredCores * rate), so they must be
    // HALF the 2-core-pack price. Earlier builds stored the full pack price here, which double-counted
    // every core and roughly doubled the owned-cost / right-sizing-savings headline. Override via the
    // Settings rows (see LoadAsync) with a customer's actual negotiated per-core rate.
    public const decimal DefaultEnterpriseCostPerCoreAnnual = 7561.50m;
    public const decimal DefaultStandardCostPerCoreAnnual = 1972.50m;
    public const decimal DefaultBlendedCostPerCore = 5500m;

    // Azure consumption (pay-as-you-go SQL license) per-vCore-hour rates. Estimates —
    // real Azure list prices change and vary by region; override via Settings. These
    // drive the "consumption vs owned per-core" comparison and the Hybrid Benefit story.
    public const string EnterprisePaygKey = "EnterprisePaygPerCoreHour";
    public const string StandardPaygKey = "StandardPaygPerCoreHour";
    public const decimal DefaultEnterprisePaygPerCoreHour = 0.2741m;
    public const decimal DefaultStandardPaygPerCoreHour = 0.0735m;
    /// <summary>Hours per month a 24x7 instance runs (730 = 365*24/12). Consumption cost scales with actual running hours.</summary>
    public const decimal HoursPerMonth24x7 = 730m;

    public sealed record CostSet(
        decimal EnterpriseCostPerCoreAnnual,
        decimal StandardCostPerCoreAnnual,
        decimal BlendedCostPerCore)
    {
        /// <summary>Per-core annual cost for an edition string ("Enterprise", "Standard Edition (64-bit)", ...). Free editions cost 0.</summary>
        public decimal PerCoreAnnualFor(string? edition)
        {
            if (string.IsNullOrEmpty(edition)) return 0m;
            if (edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)) return EnterpriseCostPerCoreAnnual;
            if (edition.Contains("Standard", StringComparison.OrdinalIgnoreCase)) return StandardCostPerCoreAnnual;
            return 0m;
        }
    }

    public static readonly CostSet Defaults = new(
        DefaultEnterpriseCostPerCoreAnnual,
        DefaultStandardCostPerCoreAnnual,
        DefaultBlendedCostPerCore);

    /// <summary>
    /// Load the cost set from the Settings table over an existing connection
    /// (Dapper opens it if closed). Missing or unparseable rows fall back to the
    /// defaults — this never throws for configuration reasons.
    /// </summary>
    public static async Task<CostSet> LoadAsync(IDbConnection conn)
    {
        var rows = await conn.QueryAsync<(string Key, string Value)>(
            "SELECT [Key], [Value] FROM Settings WHERE Category = @Category AND [Key] IN (@K1, @K2, @K3)",
            new { Category, K1 = EnterpriseKey, K2 = StandardKey, K3 = BlendedKey });

        decimal enterprise = DefaultEnterpriseCostPerCoreAnnual;
        decimal standard = DefaultStandardCostPerCoreAnnual;
        decimal blended = DefaultBlendedCostPerCore;

        foreach (var (key, value) in rows)
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                continue;
            if (string.Equals(key, EnterpriseKey, StringComparison.OrdinalIgnoreCase)) enterprise = parsed;
            else if (string.Equals(key, StandardKey, StringComparison.OrdinalIgnoreCase)) standard = parsed;
            else if (string.Equals(key, BlendedKey, StringComparison.OrdinalIgnoreCase)) blended = parsed;
        }

        return new CostSet(enterprise, standard, blended);
    }
}
