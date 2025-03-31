using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;

namespace Infrastructure.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationHub> _logger;
        private readonly IUserService _userService;
        private static readonly ConcurrentDictionary<string, string> _userConnectionMap = new();

        public NotificationHub(
            INotificationService notificationService,
            ILogger<NotificationHub> logger,
            IUserService userService)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        public override async Task OnConnectedAsync()
        {
            string userId = GetUserIdFromContext();
            string connectionId = Context.ConnectionId;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["UserId"] = userId,
                ["ConnectionId"] = connectionId,
                ["Action"] = "Connect",
                ["RequestId"] = Activity.Current?.Id ?? Context.ConnectionId
            }))
            {
                try
                {
                    // Verify user ID is present
                    if (string.IsNullOrEmpty(userId))
                    {
                        _logger.LogWarning("Authentication missing or invalid");
                        await Clients.Caller.SendAsync("AuthenticationFailed", "User identity not found in token");
                        Context.Abort();
                        return;
                    }

                    // Verify user exists in database
                    if (!await _userService.CheckUserExists(Guid.Parse(userId)))
                    {
                        _logger.LogWarning("User {UserId} does not exist in database", userId);
                        await Clients.Caller.SendAsync("AuthenticationFailed", "User not found");
                        Context.Abort();
                        return;
                    }

                    // Add user to their own group for targeted notifications
                    await Groups.AddToGroupAsync(connectionId, $"user_{userId}");

                    // Track connection in concurrent dictionary
                    _userConnectionMap.AddOrUpdate(userId, connectionId, (_, _) => connectionId);

                    // Register in notification service
                    _notificationService.AddUserToList(userId);
                    _notificationService.AddUserConnectionId(userId, connectionId);

                    _logger.LogInformation("User {UserId} connected", userId);
                    await Clients.Caller.SendAsync("Connected", connectionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in connection setup");
                    await Clients.Caller.SendAsync("Error", "Connection setup failed");
                    Context.Abort();
                }
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            string userId = GetUserIdFromContext();
            string connectionId = Context.ConnectionId;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["UserId"] = userId,
                ["ConnectionId"] = connectionId,
                ["Action"] = "Disconnect"
            }))
            {
                try
                {
                    if (!string.IsNullOrEmpty(userId))
                    {
                        // Remove from group
                        await Groups.RemoveFromGroupAsync(connectionId, $"user_{userId}");

                        // Remove from connection tracking
                        _userConnectionMap.TryRemove(userId, out _);
                        _notificationService.RemoveUserFromList(userId);

                        _logger.LogInformation("User {UserId} disconnected", userId);
                    }

                    if (exception != null)
                    {
                        _logger.LogWarning(exception, "Client disconnected with error");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during disconnect");
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        [Authorize]
        public async Task SendNotification(string userId, string message)
        {
            string senderId = GetUserIdFromContext();
            bool isAdmin = Context.User?.IsInRole("ADMIN") == true;

            // Security check: Only admins or the user themselves can send notifications to a user
            if (!isAdmin && senderId != userId)
            {
                _logger.LogWarning("Unauthorized attempt by {SenderId} to send notification to {UserId}",
                    senderId, userId);
                return;
            }

            try
            {
                // Create a persistent notification
                await _notificationService.CreateNotificationAsync(new NotificationData
                {
                    UserId = userId,
                    Message = message,
                    IsRead = false
                });

                // Send real-time notification to all user's connections
                await Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", userId, message);
                _logger.LogInformation("Notification sent to user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification to {UserId}", userId);
            }
        }

        // Secure method for testing connection
        [Authorize]
        public async Task Ping()
        {
            string userId = GetUserIdFromContext();
            await Clients.Caller.SendAsync("Pong", $"Connected as {userId}");
        }

        // Helper method to extract user ID consistently
        private string GetUserIdFromContext()
        {
            return Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                   Context.User?.FindFirst("sub")?.Value ?? string.Empty;
        }
    }
}