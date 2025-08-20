using Application.Extensions;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Notification;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly ILoggingService _logger;

        public NotificationController(
            INotificationService notificationService,
            ILoggingService logger)
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
        [HttpGet("get/all")]
        public async Task<IActionResult> GetUserNotifications(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var Scope = _logger.BeginScope();

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(currentUserId, out var userId) || userId == Guid.Empty)
            {
                return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
            }

            try
            {
                var notificationsResult = await _notificationService.GetUserNotificationsAsync(
                    userId
                    //page,
                    //pageSize
                    );

                if (notificationsResult == null || !notificationsResult.IsSuccess)
                    throw new DatabaseException(notificationsResult.ErrorMessage);

                var notifications = notificationsResult.Data.Select(n => new NotificationDto()
                {
                    Id = n.Id,
                    Message = n.Message,
                    CreatedAt = n.CreatedAt,
                    IsRead = n.IsRead
                });

                _logger.LogInformation(
                    "Retrieved {Count} notifications for user {UserId})",
                    notifications?.Count() ?? 0,
                    userId
                );

                return ResultWrapper.Success(notifications)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Error retrieving notifications for user {UserId}: {ErrorMessavge}",
                    userId,
                    ex.Message
                );

                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Marks a notification as read
        /// </summary>
        /// <param name="notificationId">Notification ID</param>
        /// <returns>Result of the operation</returns>
        [HttpPost("read/{notificationId}")]
        public async Task<IActionResult> MarkAsRead(string notificationId)
        {
            using (_logger.BeginScope(new
            {
                NotificationId = notificationId,
            }))
            {
                if (string.IsNullOrEmpty(notificationId) || !Guid.TryParse(notificationId, out var notificationGuid))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError, 
                        "Valid notification ID is required",
                        "INVALID_REQUEST")
                        .ToActionResult(this);
                }

                try
                {
                    var result = await _notificationService.MarkAsReadAsync(notificationGuid);

                    if (!result.IsSuccess)
                    {
                        // Check for a "not found" scenario
                        if (result.ErrorMessage?.Contains("not found") == true)
                        {
                            return ResultWrapper.NotFound("Notification", notificationId)
                                .ToActionResult(this);
                        }


                        if (result == null || !result.IsSuccess)
                            throw new DatabaseException(result.ErrorMessage);
                    }

                    return ResultWrapper.Success("Notification successfully marked as read").ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Error marking notification {NotificationId} as read: {ErrorMessage}",
                        notificationId,
                        ex.Message
                    );

                    return ResultWrapper.InternalServerError()
                         .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Marks all notifications as read
        /// </summary>
        /// <returns>Result of the operation</returns>
        [HttpPut("read/all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            using (_logger.BeginScope())
            {
                try
                {
                    var userId = GetUserId() ?? Guid.Empty;

                    if (userId == Guid.Empty)
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);

                    var result = await _notificationService.MarkAllAsReadAsync(userId);

                    if (result == null || !result.IsSuccess)
                    {
                        throw new Exception($"Failed to update notifications: {result.ErrorMessage}");
                    }

                    return result.ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Error marking all notifications as read: {ErrorMessage}",
                        ex.Message
                    );

                    return ResultWrapper.InternalServerError()
                         .ToActionResult(this);
                }
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

        private Guid? GetUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }
    }
}