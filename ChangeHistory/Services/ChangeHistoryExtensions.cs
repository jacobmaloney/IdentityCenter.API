using Microsoft.Extensions.DependencyInjection;

namespace ChangeHistory.Services;

/// <summary>
/// DI extension method for registering ChangeHistory services.
/// </summary>
public static class ChangeHistoryExtensions
{
    public static IServiceCollection AddChangeHistory(this IServiceCollection services)
    {
        services.AddScoped<IChangeHistoryService, ChangeHistoryService>();
        return services;
    }
}
