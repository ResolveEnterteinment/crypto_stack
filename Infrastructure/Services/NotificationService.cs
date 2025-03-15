using Application.Interfaces;
using Domain.Constants;
using Domain.DTOs;
using Infrastructure.Hubs;
using Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

public class NotificationService : BaseService<NotificationData>, INotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IUserService _userService;

    public NotificationService(
        IHubContext<NotificationHub> hubContext,
        IUserService userService,
        IOptions<MongoDbSettings> mongoDbSettings,
        IMongoClient mongoClient,
        ILogger<SubscriptionService> logger) : base(mongoClient, mongoDbSettings, "notifications", logger)
    {
        _hubContext = hubContext;
        _userService = userService;
    }

    public async Task<IEnumerable<NotificationData>> GetUserNotificationsAsync(string userId)
    {
        return await _collection.Find(n => n.UserId == userId)
                                   .SortByDescending(n => n.CreatedAt)
                                   .ToListAsync();
    }

    public async Task<ResultWrapper<InsertResult>> CreateNotificationAsync(NotificationData notification)
    {
        try
        {
            if (string.IsNullOrEmpty(notification.Message))
            {
                throw new ArgumentException("Notification message is required.");
            }

            if (!(await _userService.CheckUserExists(Guid.Parse(notification.UserId))))
            {
                throw new KeyNotFoundException("User not found.");
            }
            var insertResult = await InsertOneAsync(notification);
            // Notify the user via SignalR
            if (!insertResult.IsAcknowledged)
            {
                throw new MongoException("Failed to create notification.");
            }
            await _hubContext.Clients.User(notification.UserId).SendAsync("ReceiveNotification", notification.Message);
            return ResultWrapper<InsertResult>.Success(insertResult);
        }
        catch (Exception ex)
        {
            return ResultWrapper<InsertResult>.Failure(FailureReason.From(ex), ex.Message);
        }
    }

    public async Task<ResultWrapper<UpdateResult>> MarkAsReadAsync(Guid notificationId)
    {
        try
        {
            var update = Builders<NotificationData>.Update.Set(n => n.IsRead, true);
            var updateResult = await _collection.UpdateOneAsync(n => n.Id == notificationId, update);
            if (!updateResult.IsAcknowledged)
            {
                throw new MongoException("Database error: Unable to mark notification as read.");
            }
            return ResultWrapper<UpdateResult>.Success(updateResult);
        }
        catch (Exception ex)
        {
            return ResultWrapper<UpdateResult>.Failure(FailureReason.From(ex), ex.Message);
        }
    }
}