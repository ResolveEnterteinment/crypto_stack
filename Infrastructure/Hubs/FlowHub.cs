using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Hubs
{
    /// <summary>
    /// SignalR Hub for real-time flow updates
    /// </summary>
    [Authorize(Roles = "ADMIN")]
    public class FlowHub : Hub
    {
        private readonly ILogger<FlowHub> _logger;

        public FlowHub(ILogger<FlowHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, "flow-admins");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "flow-admins");
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Subscribe to specific flow updates
        /// </summary>
        public async Task SubscribeToFlow(Guid flowId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"flow-{flowId}");
            _logger.LogDebug("Client {ConnectionId} subscribed to flow {FlowId}", Context.ConnectionId, flowId);
        }

        /// <summary>
        /// Unsubscribe from specific flow updates
        /// </summary>
        public async Task UnsubscribeFromFlow(Guid flowId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"flow-{flowId}");
            _logger.LogDebug("Client {ConnectionId} unsubscribed from flow {FlowId}", Context.ConnectionId, flowId);
        }
    }
}
