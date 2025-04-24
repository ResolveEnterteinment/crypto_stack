using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(
            INotificationService notificationService,
            ILogger<NotificationController> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves notifications for a specific user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="page">Page number (optional)</param>
        /// <param name="pageSize">Page size (optional)</param>
        /// <returns>List of user notifications</returns>
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUserNotifications(
            string userId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest("User ID is required");
            }

            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            try
            {
                var notificationsResult = await _notificationService.GetUserNotificationsAsync(
                    userId
                    //page,
                    //pageSize
                    );
                if (notificationsResult == null || !notificationsResult.IsSuccess)
                    throw new DatabaseException(notificationsResult.ErrorMessage);

                var notifications = notificationsResult.Data;
                _logger.LogInformation(
                    "Retrieved {Count} notifications for user {UserId} (RequestID: {RequestId})",
                    notifications?.Count() ?? 0,
                    userId,
                    requestId
                );

                return Ok(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error retrieving notifications for user {UserId} (RequestID: {RequestId})",
                    userId,
                    requestId
                );

                return StatusCode(500, new { message = "An error occurred while retrieving notifications" });
            }
        }

        /// <summary>
        /// Creates a new notification for a user
        /// </summary>
        /// <param name="notification">Notification data</param>
        /// <returns>Result of the operation</returns>
        [HttpPost("create")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateNotification([FromBody] NotificationData notification)
        {
            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            // Validate input
            if (notification == null)
            {
                return BadRequest("Notification data is required");
            }

            if (string.IsNullOrEmpty(notification.Message))
            {
                return BadRequest("Notification message is required");
            }

            if (string.IsNullOrEmpty(notification.UserId))
            {
                return BadRequest("User ID is required");
            }

            try
            {
                // Create notification in database
                var insertResult = await _notificationService.CreateAndSendNotificationAsync(notification);

                if (!insertResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "Failed to create notification: {ErrorMessage} (RequestID: {RequestId})",
                        insertResult.ErrorMessage,
                        requestId
                    );

                    return BadRequest(insertResult.ErrorMessage);
                }

                return Ok(new
                {
                    id = insertResult.Data.ToString(),
                    message = "Notification created successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error creating notification (RequestID: {RequestId})",
                    requestId
                );

                return StatusCode(500, new { message = "An error occurred while creating the notification" });
            }
        }

        /// <summary>
        /// Marks a notification as read
        /// </summary>
        /// <param name="notificationId">Notification ID</param>
        /// <returns>Result of the operation</returns>
        [HttpPost("read/{notificationId}")]
        //[IgnoreAntiforgeryToken]
        public async Task<IActionResult> MarkAsRead(string notificationId)
        {
            if (string.IsNullOrEmpty(notificationId) || !Guid.TryParse(notificationId, out var notificationGuid))
            {
                return BadRequest("Valid notification ID is required");
            }

            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            try
            {
                var result = await _notificationService.MarkAsReadAsync(notificationGuid);

                if (!result.IsSuccess)
                {
                    // Check for a "not found" scenario
                    if (result.ErrorMessage?.Contains("not found") == true)
                    {
                        return NotFound(new { message = $"Notification {notificationId} not found" });
                    }

                    return BadRequest(new { message = result.ErrorMessage });
                }

                return Ok(new { message = "Notification marked as read" });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error marking notification {NotificationId} as read (RequestID: {RequestId})",
                    notificationId,
                    requestId
                );

                return StatusCode(500, new { message = "An error occurred while marking the notification as read" });
            }
        }

        /// <summary>
        /// Health check endpoint for the notification service
        /// </summary>
        /// <returns>Health status</returns>
        [HttpGet("health")]
        //[IgnoreAntiforgeryToken]
        public IActionResult HealthCheck()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                service = "NotificationService"
            });
        }
    }
}