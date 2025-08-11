using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Collections.Concurrent;

namespace Infrastructure.Services.Base
{
    public class NotificationService : INotificationService
    {
        private readonly ICrudRepository<NotificationData> _repository;
        private readonly ILoggingService _loggingService;
        private readonly IResilienceService<NotificationData> _resilienceService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IMongoIndexService<NotificationData> _indexService;
        private static readonly ConcurrentDictionary<string, string> _userConnections = new(StringComparer.OrdinalIgnoreCase);

        private const string CACHE_KEY_USER_NOTIFICATIONS = "notifications:{0}";

        private static readonly IReadOnlySet<string> _validPropertyNames;

        public NotificationService(
            ICrudRepository<NotificationData> repository,
            ILoggingService loggingService,
            IResilienceService<NotificationData> resilienceService,
            IMongoIndexService<NotificationData> indexService,
            IHubContext<NotificationHub> hubContext,
            IEnumerable<CreateIndexModel<NotificationData>>? indexModels = null
        )
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
            _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            if (indexModels != null)
            {
                _indexService.EnsureIndexesAsync(indexModels);
            }
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
                _loggingService.LogInformation("Added connection {ConnectionId} for user {UserId}", connectionId, user);
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
            => await _resilienceService.CreateBuilder<IEnumerable<NotificationData>>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Notification",
                    FileName = "NotificationService",
                    OperationName = "GetUserNotificationsAsync(string userId)",
                    State = {
                        ["UserId"] = userId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<NotificationData>.Filter.And(
                        Builders<NotificationData>.Filter.Eq(n => n.UserId, userId),
                        Builders<NotificationData>.Filter.Eq(n => n.IsRead, false)
                    );

                    var notifications = await _repository.GetAllAsync(filter) ??
                        throw new KeyNotFoundException("Failed to fetch");

                    return notifications!;
                }
            )
            .ExecuteAsync();

        public async Task<ResultWrapper<bool>> CreateAndSendNotificationAsync(NotificationData notification)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Notification",
                    FileName = "NotificationService",
                    OperationName = "CreateAndSendNotificationAsync(NotificationData notification)",
                    State = {
                        ["NotificationId"] = notification.Id,
                    },
                    LogLevel = LogLevel.Warning
                },
                async () =>
                {
                    if (notification == null)
                        throw new ArgumentNullException(nameof(notification));

                    if (string.IsNullOrWhiteSpace(notification.Message))
                        throw new ArgumentException("Notification message is required", nameof(notification.Message));

                    if (string.IsNullOrWhiteSpace(notification.UserId))
                        throw new ArgumentException("User ID is required", nameof(notification.UserId));

                    var insertResult = await _repository.InsertAsync(notification);

                    if (insertResult == null || !insertResult.IsSuccess)
                        throw new MongoException("Failed to create notification");

                    // Send real-time notification via SignalR
                    try
                    {
                        await _hubContext.Clients
                            .Group($"user-{notification.UserId}")
                            .SendAsync("ReceiveNotification", notification.UserId, notification.Message);

                        _loggingService.LogInformation(
                            "Real-time notification sent to user {UserId}",
                            notification.UserId
                        );
                    }
                    catch (Exception signalREx)
                    {
                        // Log but don't fail the operation if real-time delivery fails
                        throw new NotificationException(
                            notification,
                            signalREx
                        );
                    }

                    return true;
                })
                .WithMongoDbWriteResilience()
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> MarkAsReadAsync(Guid notificationId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Notification",
                    FileName = "NotificationService",
                    OperationName = "CreateAndSendNotificationAsync(NotificationData notification)",
                    State = {
                        ["NotificationId"] = notificationId,
                    },
                    LogLevel = LogLevel.Warning
                },
                async () =>
                {
                    var notification = await _repository.GetByIdAsync(notificationId) ??
                        throw new KeyNotFoundException($"Notification not found: Fetch notificiation returned null");

                    var result = await _repository.UpdateAsync(notificationId, new { IsRead = true }) ??
                        throw new DatabaseException($"Failed to update notification: Update result returned null.");

                    _loggingService.LogInformation("Marked notification {NotificationId} as read", notificationId);
                    return true;
                })
                .WithMongoDbWriteResilience()
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> MarkAllAsReadAsync(Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Notification",
                    FileName = "NotificationService",
                    OperationName = "CreateAndSendNotificationAsync(NotificationData notification)",
                    State = {
                        ["UserId"] = userId,
                    },
                    LogLevel = LogLevel.Warning
                },
                async () =>
                {
                    var filter = new FilterDefinitionBuilder<NotificationData>().And([
                        new FilterDefinitionBuilder<NotificationData>().Eq(n => n.UserId, userId.ToString()),
                        new FilterDefinitionBuilder<NotificationData>().Eq(n => n.IsRead, false),
                        ]);

                    var result = await _repository.UpdateManyAsync(filter, new { IsRead = true }) ??
                        throw new DatabaseException($"Failed to update notifications: Update result returned null.");

                    _loggingService.LogInformation("Marked notifications {NotificationId} as read", result.AffectedIds);
                    return true;
                })
                .WithMongoDbWriteResilience()
                .ExecuteAsync();
        }
    }
}
