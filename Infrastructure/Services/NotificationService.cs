using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.DTOs;
using Domain.Exceptions;
using Infrastructure.Hubs;
using Infrastructure.Services.Base;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Collections.Concurrent;

namespace Infrastructure.Services
{
    public class NotificationService : BaseService<NotificationData>, INotificationService
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IUserService _userService;
        private static readonly ConcurrentDictionary<string, string> _userConnections = new(StringComparer.OrdinalIgnoreCase);

        private const string CACHE_KEY_USER_NOTIFICATIONS = "notifications:{0}";

        public NotificationService(
            ICrudRepository<NotificationData> repository,
            ICacheService<NotificationData> cacheService,
            IMongoIndexService<NotificationData> indexService,
            ILoggingService logger,
            IHubContext<NotificationHub> hubContext,
            IUserService userService
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            null,
            new[]
            {
                new CreateIndexModel<NotificationData>(
                    Builders<NotificationData>.IndexKeys.Ascending(n => n.UserId),
                    new CreateIndexOptions { Name = "UserId_1" }),
                new CreateIndexModel<NotificationData>(
                    Builders<NotificationData>.IndexKeys.Descending(n => n.CreatedAt),
                    new CreateIndexOptions { Name = "CreatedAt_-1" }),
                new CreateIndexModel<NotificationData>(
                    Builders<NotificationData>.IndexKeys.Ascending(n => n.IsRead),
                    new CreateIndexOptions { Name = "IsRead_1" })
            }
        )
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        public bool AddUserToList(string userToAdd)
        {
            if (string.IsNullOrWhiteSpace(userToAdd))
                throw new ArgumentNullException(nameof(userToAdd));

            return _userConnections.TryAdd(userToAdd, null);
        }

        public void RemoveUserFromList(string user)
        {
            if (!string.IsNullOrWhiteSpace(user))
                _userConnections.TryRemove(user, out _);
        }

        public void AddUserConnectionId(string user, string connectionId)
        {
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(connectionId))
            {
                _userConnections[user] = connectionId;
                Logger.LogInformation("Added connection {ConnectionId} for user {UserId}", connectionId, user);
            }
        }

        public string GetById(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                return null;

            return _userConnections.FirstOrDefault(x => x.Value == connectionId).Key;
        }

        public string GetByUser(string user)
        {
            if (string.IsNullOrWhiteSpace(user) || !_userConnections.TryGetValue(user, out var connectionId))
                return null;

            return connectionId;
        }

        public async Task<ResultWrapper<IEnumerable<NotificationData>>> GetUserNotificationsAsync(string userId)
            => await SafeExecute<IEnumerable<NotificationData>>(
                async () =>
                {
                    var filter = Builders<NotificationData>.Filter.And(
                        Builders<NotificationData>.Filter.Eq(n => n.UserId, userId),
                        Builders<NotificationData>.Filter.Eq(n => n.IsRead, false)
                    );

                    var notifications = await _repository.GetAllAsync(filter) ??
                        throw new KeyNotFoundException("Failed to fetch");
                    Logger.LogInformation("Retrieved {Count} unread notifications for user {UserId}", notifications?.Count ?? 0, userId);

                    return notifications!;
                }
            );

        public async Task<ResultWrapper> CreateAndSendNotificationAsync(NotificationData notification)
        {
            try
            {
                if (notification == null)
                    throw new ArgumentNullException(nameof(notification));

                if (string.IsNullOrWhiteSpace(notification.Message))
                    throw new ArgumentException("Notification message is required", nameof(notification.Message));

                if (string.IsNullOrWhiteSpace(notification.UserId))
                    throw new ArgumentException("User ID is required", nameof(notification.UserId));

                if (!Guid.TryParse(notification.UserId, out var uid) || !await _userService.CheckUserExists(uid))
                    throw new KeyNotFoundException($"User with ID {notification.UserId} not found");

                if (notification.Id == Guid.Empty)
                    notification.Id = Guid.NewGuid();

                if (notification.CreatedAt == default)
                    notification.CreatedAt = DateTime.UtcNow;

                var insertResult = await InsertAsync(notification);

                if (insertResult == null || !insertResult.IsSuccess || !insertResult.Data.IsSuccess)
                    throw new MongoException("Failed to create notification");

                try
                {
                    // Send real-time notification via SignalR
                    try
                    {
                        await _hubContext.Clients
                            .Group($"user-{notification.UserId}")
                            .SendAsync("ReceiveNotification", notification.UserId, notification.Message);

                        Logger.LogInformation(
                            "Real-time notification sent to user {UserId}",
                            notification.UserId
                        );
                    }
                    catch (Exception signalREx)
                    {
                        // Log but don't fail the operation if real-time delivery fails
                        Logger.LogWarning(
                            "Failed to send real-time notification to user {UserId}: {ErrorMessage}",
                            notification.UserId,
                            signalREx.Message
                        );
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Failed to send real-time notification to user {UserId}", notification.UserId);
                }

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to create notification: {Message}", ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper> MarkAsReadAsync(Guid notificationId)
        {
            using (Logger.BeginScope("NotificationService::MarkAsReadAsync", new Dictionary<string, object>
            {
                ["NotificationId"] = notificationId,
            }))
            {
                try
                {
                    var notification = await _repository.GetByIdAsync(notificationId) ??
                        throw new KeyNotFoundException($"Notification not found: Fetch notificiation returned null");

                    var result = await _repository.UpdateAsync(notificationId, new { IsRead = true }) ??
                        throw new DatabaseException($"Failed to update notification: Update result returned null.");

                    Logger.LogInformation("Marked notification {NotificationId} as read", notificationId);
                    return ResultWrapper.Success("Marked notification {NotificationId} as read");
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to mark notification {NotificationId} as read: {Message}", notificationId, ex.Message);
                    return ResultWrapper.FromException(ex);
                }
            }
        }
    }
}
