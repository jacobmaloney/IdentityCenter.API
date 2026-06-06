using System.Security.Claims;
using DataAccessLibrary.Services.Modules;

namespace DataAccessLibrary.Services;

/// <summary>
/// Module gate. Backed by Settings(Category='Modules', Key=&lt;moduleKey&gt;).
/// Default-off — a missing row reads as disabled.
/// </summary>
public interface IModuleGate
{
    Task<bool> IsEnabledAsync(string moduleKey, CancellationToken ct = default);
    Task<IReadOnlyList<ModuleState>> GetAllAsync(CancellationToken ct = default);
    Task SetEnabledAsync(string moduleKey, bool enabled, ClaimsPrincipal user, CancellationToken ct = default);
}

public record ModuleState(ModuleDefinition Definition, bool Enabled, DateTime? LastModifiedAt, string? LastModifiedBy);
