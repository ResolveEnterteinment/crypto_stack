using Application.Interfaces;
using Domain.DTOs;
using Infrastructure.Hubs;
using Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

public class NotificationService : BaseService<NotificationData>, INotificationService
{
    public readonly IHubContext<NotificationHub> _hubContext;
    private readonly IUserService _userService;
    private static readonly Dictionary<string, string> _users = new Dictionary<string, string>();

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

    public bool AddUserToList(string userToAdd)
    {
        if (string.IsNullOrWhiteSpace(userToAdd))
        {
            throw new ArgumentNullException(nameof(userToAdd));
        }
        lock (_users)
        {
            foreach (var user in _users)
            {
                if (user.Key.ToLower() == userToAdd.ToLower()) // if user name exsist
                    return false;
            }
            _users.Add(userToAdd, null);
            return true;
        }
    }

    public void RemoveUserFromList(string user)
    {
        lock (_users)
        {
            if (_users.ContainsKey(user))
            {
                _users.Remove(user);
            }
        }
    }

    public void AddUserConnectionId(string user, string connectionId)
    {
        lock (_users)
        {
            bool res = _users.ContainsKey(user);
            if (res)
            {
                _users[user] = connectionId;
            }
        }
    }

    public string GetUserConnectionById(string connectionId)
    {
        lock (_users)
        {
            var res = _users.Where(x => x.Value == connectionId).Select(x => x.Key).FirstOrDefault();
            return res;
        }
    }

    public string GetUserConnectionByUser(string user)
    {
        lock (_users)
        {
            return _users.Where(x => x.Key == user).Select(x => x.Value).FirstOrDefault();
        }
    }

    public async Task<IEnumerable<NotificationData>> GetUserNotificationsAsync(string userId)
    {
        return await _collection.Find(n => n.UserId == userId && n.IsRead == false)
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

            if (!insertResult.IsAcknowledged)
            {
                throw new MongoException("Failed to create notification.");
            }

            // ✅ Ensure user has a valid connection ID before sending
            var clientId = GetUserConnectionByUser(notification.UserId);
            if (!string.IsNullOrEmpty(clientId))
            {
                await _hubContext.Clients.Client(clientId).SendAsync("ReceiveNotification", notification.UserId, notification.Message);
                _logger.LogInformation($"✅ Notification sent to user {notification.UserId}");
            }
            else
            {
                _logger.LogWarning($"⚠️ User {notification.UserId} has no active SignalR connection.");
            }

            return ResultWrapper<InsertResult>.Success(insertResult);
        }
        catch (Exception ex)
        {
            return ResultWrapper<InsertResult>.FromException(ex);
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
            return ResultWrapper<UpdateResult>.FromException(ex);
        }
    }
}