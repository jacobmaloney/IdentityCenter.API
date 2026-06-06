using Microsoft.Extensions.DependencyInjection;
using DataAccessLibrary.Services.PersonMatching.Strategies;

namespace DataAccessLibrary.Services.PersonMatching;

/// <summary>
/// Extension methods for registering person matching services.
/// </summary>
public static class PersonMatchingServiceExtensions
{
    /// <summary>
    /// Adds enterprise person matching services to the DI container.
    /// </summary>
    public static IServiceCollection AddPersonMatchingServices(this IServiceCollection services)
    {
        // Register the main service
        services.AddScoped<IPersonMatchingService, PersonMatchingService>();

        // Register individual strategies (optional - service creates them internally)
        services.AddTransient<CompositeMatchingStrategy>();
        services.AddTransient<ConfigurableMatchingStrategy>();
        services.AddTransient<EmailMatchingStrategy>();
        services.AddTransient<EmployeeIdMatchingStrategy>();
        services.AddTransient<UPNMatchingStrategy>();
        services.AddTransient<UsernameMatchingStrategy>();
        services.AddTransient<NameMatchingStrategy>();

        return services;
    }
}
