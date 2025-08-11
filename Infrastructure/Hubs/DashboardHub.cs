using Application.Interfaces;
using Domain.Models.Asset;
using Domain.Models.Balance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Security.Claims;

namespace Infrastructure.Hubs
{
    [Authorize]
    public class DashboardHub : Hub
    {
        private readonly ILogger<DashboardHub> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public DashboardHub(
            ILogger<DashboardHub> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        /// <summary>
        /// Subscribe to dashboard updates for a specific user
        /// </summary>
        public async Task SubscribeToUpdates(string userId)
        {
            try
            {
                // Security check - users can only subscribe to their own updates
                var currentUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                bool isAdmin = Context.User?.IsInRole("ADMIN") ?? false;

                if (string.IsNullOrEmpty(currentUserId))
                {
                    _logger.LogWarning("Authentication required to subscribe to dashboard updates");
                    throw new HubException("Authentication required to subscribe to dashboard updates");
                }

                // Admin can subscribe to any user, regular users only to themselves
                if (!isAdmin && currentUserId != userId)
                {
                    _logger.LogWarning("User {CurrentUserId} attempted to subscribe to dashboard for user {TargetUserId}",
                        currentUserId, userId);
                    throw new HubException("You can only subscribe to your own dashboard updates");
                }

                // Parse userId to Guid
                if (!Guid.TryParse(userId, out var userGuid))
                {
                    throw new HubException("Invalid user ID format");
                }

                // Add the user to a group for targeted updates
                string groupName = userId;
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                _logger.LogInformation("User {UserId} subscribed to dashboard updates with connection {ConnectionId}",
                    userId, Context.ConnectionId);

                // Get dashboard service when needed to avoid circular dependency
                using var scope = _scopeFactory.CreateScope();
                var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

                // Send initial dashboard data
                var dashboardData = await dashboardService.GetDashboardDataAsync(userGuid);
                if (dashboardData.IsSuccess && dashboardData.Data != null)
                {
                    await Clients.Caller.SendAsync("DashboardUpdate", dashboardData.Data);
                    _logger.LogDebug("Sent initial dashboard data to user {UserId}", userId);
                }
                else
                {
                    _logger.LogWarning("Failed to fetch initial dashboard data for user {UserId}: {ErrorMessage}",
                        userId, dashboardData.ErrorMessage);
                    await Clients.Caller.SendAsync("DashboardError", "Failed to load dashboard data");
                }

                // Send acknowledgment
                await Clients.Caller.SendAsync("SubscriptionConfirmed", Context.ConnectionId);
            }
            catch (Exception ex) when (!(ex is HubException))
            {
                _logger.LogError(ex, "Error subscribing to dashboard updates for user {UserId}", userId);
                throw new HubException($"Error subscribing to dashboard updates: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle client connection
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            _logger.LogInformation("User {UserId} connected to DashboardHub with connection ID {ConnectionId}",
                userId ?? "unknown", Context.ConnectionId);

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Handle client disconnection
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (exception != null)
            {
                _logger.LogWarning(exception, "User {UserId} disconnected from DashboardHub with error, connection ID {ConnectionId}",
                    userId ?? "unknown", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("User {UserId} disconnected from DashboardHub, connection ID {ConnectionId}",
                    userId ?? "unknown", Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Manually refresh dashboard data
        /// </summary>
        public async Task RefreshDashboard()
        {
            try
            {
                var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
                {
                    throw new HubException("Authentication required");
                }

                // Get dashboard service when needed
                using var scope = _scopeFactory.CreateScope();
                var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

                // Force refresh by invalidating cache and fetching new data
                dashboardService.InvalidateDashboardCacheAsync(userGuid);

                var dashboardData = await dashboardService.GetDashboardDataAsync(userGuid);
                if (dashboardData.IsSuccess && dashboardData.Data != null)
                {
                    await Clients.Caller.SendAsync("DashboardUpdate", dashboardData.Data);
                    _logger.LogInformation("Manual dashboard refresh completed for user {UserId}", userId);
                }
                else
                {
                    await Clients.Caller.SendAsync("DashboardError", "Failed to refresh dashboard data");
                    _logger.LogWarning("Manual dashboard refresh failed for user {UserId}: {ErrorMessage}",
                        userId, dashboardData.ErrorMessage);
                }
            }
            catch (Exception ex) when (!(ex is HubException))
            {
                _logger.LogError(ex, "Error during manual dashboard refresh");
                throw new HubException($"Error refreshing dashboard: {ex.Message}");
            }
        }
    }
}