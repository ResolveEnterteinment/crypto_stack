using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Infrastructure.Hubs
{
    /// <summary>
    /// SignalR hub for real-time notifications
    /// </summary>
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(ILogger<NotificationHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Adds a user to their personal notification group
        /// </summary>
        /// <param name="userId">User ID to join group for</param>
        public async Task JoinUserGroup(string userId)
        {
            try
            {
                // Security check - users can only join their own group
                var currentUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(currentUserId))
                {
                    _logger.LogWarning(
                        "User ID missing from claims for connection {ConnectionId}",
                        Context.ConnectionId
                    );

                    throw new HubException("Authentication required to join notification groups");
                }

                // Admin can join any group, regular users can only join their own
                bool isAdmin = Context.User?.IsInRole("ADMIN") ?? false;

                if (!isAdmin && currentUserId != userId)
                {
                    _logger.LogWarning(
                        "User {CurrentUserId} attempted to join group for user {TargetUserId}",
                        currentUserId,
                        userId
                    );

                    throw new HubException("You can only subscribe to your own notifications");
                }

                // Join the user-specific group
                string groupName = $"user-{userId}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

                _logger.LogInformation(
                    "User {UserId} joined notification group {GroupName} with connection {ConnectionId}",
                    currentUserId,
                    groupName,
                    Context.ConnectionId
                );

                // Send acknowledgment to the client
                await Clients.Caller.SendAsync(
                    "Connected",
                    Context.ConnectionId
                );
            }
            catch (Exception ex) when (!(ex is HubException))
            {
                _logger.LogError(
                    ex,
                    "Error joining user group for user {UserId}, connection {ConnectionId}",
                    userId,
                    Context.ConnectionId
                );

                throw new HubException($"Error joining notification group: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles client connection
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            _logger.LogInformation(
                "User {UserId} connected with connection ID {ConnectionId}",
                userId ?? "unknown",
                Context.ConnectionId
            );

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Handles client disconnection
        /// </summary>
        /// <param name="exception">Exception that caused disconnection (if any)</param>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (exception != null)
            {
                _logger.LogWarning(
                    exception,
                    "User {UserId} disconnected with error, connection ID {ConnectionId}",
                    userId ?? "unknown",
                    Context.ConnectionId
                );
            }
            else
            {
                _logger.LogInformation(
                    "User {UserId} disconnected normally, connection ID {ConnectionId}",
                    userId ?? "unknown",
                    Context.ConnectionId
                );
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}