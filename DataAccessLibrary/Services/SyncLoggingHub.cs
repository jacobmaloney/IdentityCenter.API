using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// SignalR Hub for real-time sync execution log streaming.
    /// Allows UI clients to subscribe to trace-level logging for specific sync runs.
    /// </summary>
    public class SyncLoggingHub : Hub
    {
        private readonly ILogger<SyncLoggingHub> _logger;

        public SyncLoggingHub(ILogger<SyncLoggingHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Subscribes the current connection to receive logs for a specific sync run.
        /// </summary>
        /// <param name="syncRunId">The ID of the sync run to monitor</param>
        public async Task SubscribeToSyncRun(string syncRunId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"SyncRun_{syncRunId}");
            _logger.LogDebug("Client {ConnectionId} subscribed to sync run {RunId}",
                Context.ConnectionId, syncRunId);
        }

        /// <summary>
        /// Unsubscribes the current connection from a sync run's logs.
        /// </summary>
        /// <param name="syncRunId">The ID of the sync run to stop monitoring</param>
        public async Task UnsubscribeFromSyncRun(string syncRunId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"SyncRun_{syncRunId}");
            _logger.LogDebug("Client {ConnectionId} unsubscribed from sync run {RunId}",
                Context.ConnectionId, syncRunId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogDebug("Client {ConnectionId} disconnected", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
