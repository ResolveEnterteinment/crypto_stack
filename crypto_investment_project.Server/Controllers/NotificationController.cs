using Infrastructure.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly IHubContext<NotificationHub> _notificationHub;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(
            INotificationService notificationService,
            IHubContext<NotificationHub> notificationHub,
            ILogger<NotificationController> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _notificationHub = notificationHub ?? throw new ArgumentNullException(nameof(notificationHub));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUserNotifications(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest("User ID is required");
            }

            var requestId = Request.Headers["X-Request-ID"].ToString() ?? Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            try
            {
                var notifications = await _notificationService.GetUserNotificationsAsync(userId);
                _logger.LogInformation("Retrieved {Count} notifications for user {UserId} (RequestID: {RequestId})",
                    notifications?.Count() ?? 0, userId, requestId);

                return Ok(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving notifications for user {UserId} (RequestID: {RequestId})",
                    userId, requestId);
                return StatusCode(500, "An error occurred while retrieving notifications");
            }
        }

        [HttpPost("create")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateNotification([FromBody] NotificationData notification)
        {
            var requestId = Request.Headers["X-Request-ID"].ToString() ?? Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            // Validate input
            if (notification == null)
            {
                _logger.LogWarning("Null notification data received (RequestID: {RequestId})", requestId);
                return BadRequest("Notification data is required");
            }

            if (string.IsNullOrEmpty(notification.Message))
            {
                return BadRequest("Notification message is required");
            }

            if (string.IsNullOrEmpty(notification.UserId))
            {
                return BadRequest("Notification user id is required");
            }

            try
            {
                var insertResult = await _notificationService.CreateNotificationAsync(notification);
                if (!insertResult.IsSuccess)
                {
                    _logger.LogWarning("Failed to create notification: {ErrorMessage} (RequestID: {RequestId})",
                        insertResult.ErrorMessage, requestId);
                    return BadRequest(insertResult.ErrorMessage);
                }

                // Try to send real-time notification via SignalR
                try
                {
                    await _notificationHub.Clients.Group($"user-{notification.UserId}")
                        .SendAsync("ReceiveNotification", notification.UserId, notification.Message);

                    _logger.LogInformation("Real-time notification sent to user {UserId} (RequestID: {RequestId})",
                        notification.UserId, requestId);
                }
                catch (Exception signalREx)
                {
                    // Log but don't fail the whole operation if SignalR delivery fails
                    _logger.LogWarning(signalREx, "Failed to send real-time notification to user {UserId} (RequestID: {RequestId})",
                        notification.UserId, requestId);
                }

                return Ok($"Notification {insertResult.Data.InsertedId} created successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification (RequestID: {RequestId})", requestId);
                return StatusCode(500, "An error occurred while creating the notification");
            }
        }

        [HttpPost("read/{notificationId}")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> MarkAsRead(string notificationId)
        {
            var requestId = Request.Headers["X-Request-ID"].ToString() ?? Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            // Validate input
            if (string.IsNullOrEmpty(notificationId))
            {
                _logger.LogWarning("Empty notification ID received (RequestID: {RequestId})", requestId);
                return BadRequest("Notification ID is required");
            }

            try
            {
                // Safely parse the GUID
                if (!Guid.TryParse(notificationId, out var notificationGuid) || notificationGuid == Guid.Empty)
                {
                    _logger.LogWarning("Invalid notification ID format: {NotificationId} (RequestID: {RequestId})",
                        notificationId, requestId);
                    return BadRequest("Invalid notification ID format");
                }

                var markAsReadResult = await _notificationService.MarkAsReadAsync(notificationGuid);
                if (!markAsReadResult.IsSuccess)
                {
                    // Check for a "not found" scenario - return 404 instead of 400
                    if (markAsReadResult.ErrorMessage?.Contains("not found") == true)
                    {
                        _logger.LogInformation("Notification not found: {NotificationId} (RequestID: {RequestId})",
                            notificationId, requestId);
                        return NotFound($"Notification {notificationId} not found");
                    }

                    _logger.LogWarning("Failed to mark notification as read: {ErrorMessage} (RequestID: {RequestId})",
                        markAsReadResult.ErrorMessage, requestId);
                    return BadRequest(markAsReadResult.ErrorMessage);
                }

                return Ok("Notification successfully marked as read.");
            }
            catch (FormatException)
            {
                _logger.LogWarning("Invalid notification ID format: {NotificationId} (RequestID: {RequestId})",
                    notificationId, requestId);
                return BadRequest("Invalid notification ID format");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {NotificationId} as read (RequestID: {RequestId})",
                    notificationId, requestId);
                return StatusCode(500, "An error occurred while marking the notification as read");
            }
        }

        [HttpGet("health")]
        [IgnoreAntiforgeryToken]
        public IActionResult HealthCheck()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
    }
}