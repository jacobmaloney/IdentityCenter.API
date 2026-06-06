namespace DataAccessLibrary.Services.Modules;

public enum ModuleStatus { Live, Beta, Aspirational }

public record ModuleDefinition(
    string Key,
    string DisplayName,
    string Description,
    string Icon,
    string IconAccent,
    ModuleStatus Status,
    bool DefaultEnabled,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Surfaces);

public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDefinition> All = new[]
    {
        new ModuleDefinition(
            Key: "LicenseManagement",
            DisplayName: "License Management",
            Description: "M365/Entra/SQL license inventory, threshold alerting, auto-trigger access review loop, and forecast pipeline.",
            Icon: "fa-key",
            IconAccent: "cyan",
            Status: ModuleStatus.Live,
            DefaultEnabled: false,
            DependsOn: Array.Empty<string>(),
            Surfaces: new[] {
                "License Center", "License Pool Detail", "SQL Servers",
                "Network Discovery", "Object → Licenses tab",
                "License Threshold Monitor (Quartz)", "License Snapshot (Quartz)",
                "SQL Compliance Check (Quartz)"
            }),
        new ModuleDefinition(
            Key: "EnterpriseApps",
            DisplayName: "Enterprise Apps",
            Description: "Entra app registration + service principal inventory and governance.",
            Icon: "fa-cube",
            IconAccent: "violet",
            Status: ModuleStatus.Live,
            DefaultEnabled: false,
            DependsOn: Array.Empty<string>(),
            Surfaces: new[] {
                "Enterprise Apps Center", "License Overview → Enterprise Apps card",
                "Object → Enterprise Apps surfaces"
            }),
        new ModuleDefinition(
            Key: "MachineLearning",
            DisplayName: "Machine Learning",
            Description: "ML training, risk scoring, peer outlier detection, license exhaustion forecasting.",
            Icon: "fa-brain",
            IconAccent: "amber",
            Status: ModuleStatus.Beta,
            DefaultEnabled: false,
            DependsOn: Array.Empty<string>(),
            Surfaces: new[] {
                "Model Training Dashboard", "Intelligence Hub anomaly insights",
                "ML Prediction Backfill (Quartz)", "ML Drift Detection (Quartz)",
                "Risk Engine ML features"
            }),
    };

    public static ModuleDefinition? Find(string key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
}
