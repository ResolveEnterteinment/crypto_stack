using Application.Interfaces;
using Domain.DTOs;
using Domain.DTOs.Settings;
using Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
            IHubContext<NotificationHub> hubContext,
            IUserService userService,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<NotificationService> logger,
            IMemoryCache cache
            ) : base(
                mongoClient,
                mongoDbSettings,
                "notifications",
                logger,
                cache,
                new List<CreateIndexModel<NotificationData>>
                {
                    new CreateIndexModel<NotificationData>(
                        Builders<NotificationData>.IndexKeys.Ascending(x => x.UserId),
                        new CreateIndexOptions { Name = "UserId_1" }
                    ),
                    new CreateIndexModel<NotificationData>(
                        Builders<NotificationData>.IndexKeys.Descending(x => x.CreatedAt),
                        new CreateIndexOptions { Name = "CreatedAt_-1" }
                    ),
                    new CreateIndexModel<NotificationData>(
                        Builders<NotificationData>.IndexKeys.Ascending(x => x.IsRead),
                        new CreateIndexOptions { Name = "IsRead_1" }
                    )
                }
            )
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        public bool AddUserToList(string userToAdd)
        {
            if (string.IsNullOrWhiteSpace(userToAdd))
            {
                throw new ArgumentNullException(nameof(userToAdd));
            }

            // No need for a lock with ConcurrentDictionary
            return _userConnections.TryAdd(userToAdd, null);
        }

        public void RemoveUserFromList(string user)
        {
            if (!string.IsNullOrWhiteSpace(user))
            {
                _userConnections.TryRemove(user, out _);
            }
        }

        public void AddUserConnectionId(string user, string connectionId)
        {
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(connectionId))
            {
                _userConnections[user] = connectionId;
                _logger.LogDebug("Added connection {ConnectionId} for user {UserId}", connectionId, user);
            }
        }

        public string GetById(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return null;
            }

            return _userConnections.FirstOrDefault(x => x.Value == connectionId).Key;
        }

        public string GetByUser(string user)
        {
            if (string.IsNullOrWhiteSpace(user) || !_userConnections.TryGetValue(user, out var connectionId))
            {
                return null;
            }

            return connectionId;
        }

        public async Task<IEnumerable<NotificationData>> GetUserNotificationsAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentNullException(nameof(userId), "User ID cannot be null or empty");
            }

            string cacheKey = string.Format(CACHE_KEY_USER_NOTIFICATIONS, userId);

            return await GetOrCreateCachedItemAsync<IEnumerable<NotificationData>>(
                cacheKey,
                async () =>
                {
                    var filter = Builders<NotificationData>.Filter.And(
                        Builders<NotificationData>.Filter.Eq(n => n.UserId, userId),
                        Builders<NotificationData>.Filter.Eq(n => n.IsRead, false)
                    );

                    var sort = Builders<NotificationData>.Sort.Descending(n => n.CreatedAt);

                    var notifications = await _collection.Find(filter)
                        .Sort(sort)
                        .ToListAsync();

                    _logger.LogInformation("Retrieved {Count} unread notifications for user {UserId}",
                        notifications.Count, userId);

                    return notifications;
                },
                TimeSpan.FromMinutes(1) // Short cache time for notifications
            );
        }

        public async Task<ResultWrapper<InsertResult>> CreateNotificationAsync(NotificationData notification)
        {
            try
            {
                // Validate input
                if (notification == null)
                {
                    throw new ArgumentNullException(nameof(notification), "Notification cannot be null");
                }

                if (string.IsNullOrEmpty(notification.Message))
                {
                    throw new ArgumentException("Notification message is required.", nameof(notification.Message));
                }

                if (string.IsNullOrEmpty(notification.UserId))
                {
                    throw new ArgumentException("User ID is required.", nameof(notification.UserId));
                }

                // Make sure user exists
                bool userExists = await _userService.CheckUserExists(Guid.Parse(notification.UserId));
                if (!userExists)
                {
                    throw new KeyNotFoundException($"User with ID {notification.UserId} not found.");
                }

                // Set defaults if not specified
                if (notification.Id == Guid.Empty)
                {
                    notification.Id = Guid.NewGuid();
                }

                if (notification.CreatedAt == default)
                {
                    notification.CreatedAt = DateTime.UtcNow;
                }

                // Insert notification
                var insertResult = await InsertOneAsync(notification);

                if (!insertResult.IsAcknowledged)
                {
                    throw new MongoException("Failed to create notification.");
                }

                // Invalidate cache
                _cache.Remove(string.Format(CACHE_KEY_USER_NOTIFICATIONS, notification.UserId));

                // Try to send real-time notification via SignalR
                try
                {
                    // Get the user's connection ID
                    var clientId = GetByUser(notification.UserId);

                    if (!string.IsNullOrEmpty(clientId))
                    {
                        // Send to specific client if we have their connection
                        await _hubContext.Clients.Client(clientId)
                            .SendAsync("ReceiveNotification", notification.UserId, notification.Message);

                        _logger.LogInformation("Real-time notification sent to user {UserId} via connection {ConnectionId}",
                            notification.UserId, clientId);
                    }
                    else
                    {
                        // Fall back to sending via user group if we don't have a direct connection
                        string groupName = $"user-{notification.UserId}";
                        await _hubContext.Clients.Group(groupName)
                            .SendAsync("ReceiveNotification", notification.UserId, notification.Message);

                        _logger.LogInformation("Real-time notification sent to user {UserId} via group",
                            notification.UserId);
                    }
                }
                catch (Exception signalREx)
                {
                    // Log but don't fail the operation if real-time delivery fails
                    _logger.LogWarning(signalREx,
                        "Failed to send real-time notification to user {UserId}, notification will be delivered on next poll",
                        notification.UserId);
                }

                return ResultWrapper<InsertResult>.Success(insertResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create notification: {Message}", ex.Message);
                return ResultWrapper<InsertResult>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<UpdateResult>> MarkAsReadAsync(Guid notificationId)
        {
            try
            {
                // Get the notification to identify the user for cache invalidation
                var notification = await GetByIdAsync(notificationId);
                if (notification == null)
                {
                    throw new KeyNotFoundException($"Notification with ID {notificationId} not found");
                }

                // Create update
                var update = Builders<NotificationData>.Update.Set(n => n.IsRead, true);
                var updateResult = await UpdateOneAsync(notificationId, new { IsRead = true });

                if (updateResult.ModifiedCount > 0)
                {
                    // Invalidate cache
                    _cache.Remove(string.Format(CACHE_KEY_USER_NOTIFICATIONS, notification.UserId));

                    _logger.LogInformation("Marked notification {NotificationId} as read for user {UserId}",
                        notificationId, notification.UserId);
                }

                return ResultWrapper<UpdateResult>.Success(updateResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark notification {NotificationId} as read: {Message}",
                    notificationId, ex.Message);
                return ResultWrapper<UpdateResult>.FromException(ex);
            }
        }
    }
}