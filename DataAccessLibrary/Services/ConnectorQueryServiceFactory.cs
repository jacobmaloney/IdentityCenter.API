using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Factory that routes directory query operations to the correct connector implementation
/// based on the connection type string (e.g., "ActiveDirectory", "EntraID").
/// </summary>
public class ConnectorQueryServiceFactory
{
    private readonly Dictionary<string, IDirectoryConnectorQueryService> _services;
    private readonly ILogger<ConnectorQueryServiceFactory> _logger;

    public ConnectorQueryServiceFactory(
        IEnumerable<IDirectoryConnectorQueryService> services,
        ILogger<ConnectorQueryServiceFactory> logger)
    {
        _logger = logger;
        _services = new Dictionary<string, IDirectoryConnectorQueryService>(StringComparer.OrdinalIgnoreCase);

        foreach (var service in services)
        {
            _services[service.ConnectionType] = service;
            _logger.LogDebug("Registered connector query service for type: {ConnectionType}", service.ConnectionType);
        }
    }

    /// <summary>
    /// Returns the query service for the specified connection type.
    /// </summary>
    public IDirectoryConnectorQueryService GetService(string connectionType)
    {
        if (_services.TryGetValue(connectionType, out var service))
        {
            return service;
        }

        throw new NotSupportedException(
            $"No directory connector query service registered for connection type '{connectionType}'. " +
            $"Available types: {string.Join(", ", _services.Keys)}");
    }

    /// <summary>
    /// Returns true if a query service is registered for the given connection type.
    /// </summary>
    public bool HasService(string connectionType)
    {
        return _services.ContainsKey(connectionType);
    }
}
