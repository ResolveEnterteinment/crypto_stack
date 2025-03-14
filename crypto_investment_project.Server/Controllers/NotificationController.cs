using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetUserNotifications(string userId)
    {
        var notifications = await _notificationService.GetUserNotificationsAsync(userId);
        return Ok(notifications);
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateNotification([FromBody] NotificationData notification)
    {
        if (notification == null)
        {
            BadRequest("Notification data is required");
        }
        if (string.IsNullOrEmpty(notification.Message))
        {
            BadRequest("Notification message is required");
        }
        if (string.IsNullOrEmpty(notification.UserId))
        {
            BadRequest("Notification user id is required");
        }

        var insertResult = await _notificationService.CreateNotificationAsync(notification);
        if (!insertResult.IsSuccess)
        {
            return BadRequest(insertResult.ErrorMessage);
        }
        return Ok($"Notificiation {insertResult.Data.InsertedId} created successfully.");
    }

    [HttpPost("read/{notificationId}")]
    public async Task<IActionResult> MarkAsRead(string notificationId)
    {
        if (string.IsNullOrEmpty(notificationId))
        {
            BadRequest("Notification id is required");
        }
        var notificationGuid = Guid.Parse(notificationId);
        if (notificationGuid == Guid.Empty)
        {
            BadRequest("Invalid notification id.");
        }
        var markAsReadResult = await _notificationService.MarkAsReadAsync(notificationGuid);
        if (!markAsReadResult.IsSuccess)
        {
            return BadRequest(markAsReadResult.ErrorMessage);
        }
        return Ok("Notification successfully marked as read.");
    }
}