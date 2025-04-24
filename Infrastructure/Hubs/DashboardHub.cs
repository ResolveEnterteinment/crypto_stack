// Let's enhance DashboardHub to handle updates more effectively
// In Infrastructure/Hubs/DashboardHub.cs

using Application.Interfaces;
using Domain.Models.Asset;
using Domain.Models.Balance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Security.Claims;

namespace Infrastructure.Hubs
{
    [Authorize]
    public class DashboardHub : Hub
    {
        private readonly IMongoCollection<BalanceData> _balances;
        private readonly IMongoCollection<AssetData> _assets;
        private readonly IDashboardService _dashboardService;
        private readonly ILogger<DashboardHub> _logger;

        public DashboardHub(
            IMongoDatabase database,
            IDashboardService dashboardService,
            ILogger<DashboardHub> logger)
        {
            _balances = database.GetCollection<BalanceData>("balances");
            _assets = database.GetCollection<AssetData>("assets");
            _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Start monitoring for balance changes
            StartChangeStream();
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
                string groupName = $"dashboard-{userId}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                _logger.LogInformation("User {UserId} subscribed to dashboard updates with connection {ConnectionId}",
                    userId, Context.ConnectionId);

                // Send initial dashboard data
                var dashboardData = await _dashboardService.GetDashboardDataAsync(userGuid);
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

                // Force refresh by invalidating cache and fetching new data
                await _dashboardService.InvalidateDashboardCacheAsync(userGuid);

                // This will trigger pulling fresh data and sending it to the client
                _logger.LogInformation("Manual dashboard refresh requested by user {UserId}", userId);
            }
            catch (Exception ex) when (!(ex is HubException))
            {
                _logger.LogError(ex, "Error during manual dashboard refresh");
                throw new HubException($"Error refreshing dashboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts monitoring for balance changes using MongoDB change streams
        /// </summary>
        private void StartChangeStream()
        {
            try
            {
                var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BalanceData>>()
                    .Match(change =>
                        change.OperationType == ChangeStreamOperationType.Insert ||
                        change.OperationType == ChangeStreamOperationType.Update ||
                        change.OperationType == ChangeStreamOperationType.Replace);

                var options = new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup };
                var cursor = _balances.Watch(pipeline, options);

                // Process balance changes in a background task
                Task.Run(async () =>
                {
                    try
                    {
                        // Keep watching for changes continuously
                        await cursor.ForEachAsync(async change =>
                        {
                            try
                            {
                                var updatedBalance = change.FullDocument;
                                if (updatedBalance == null)
                                {
                                    _logger.LogWarning("Received change notification with null document");
                                    return;
                                }

                                // Get the user ID and invalidate dashboard cache
                                var userId = updatedBalance.UserId;

                                // Invalidate dashboard cache and send updates
                                await _dashboardService.InvalidateDashboardCacheAsync(userId);

                                _logger.LogDebug("Dashboard updated for user {UserId} due to balance change", userId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing balance change notification");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in balance change stream. Restarting monitor...");
                        // Attempt to restart the change stream after a delay
                        await Task.Delay(5000);
                        StartChangeStream();
                    }
                });

                _logger.LogInformation("Started balance change stream monitor");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start balance change stream monitor");
            }
        }
    }
}