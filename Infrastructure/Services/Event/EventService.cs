using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.DTOs;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Event;
using Infrastructure.Services.Base;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Infrastructure.Services.Event
{
    public class EventService : BaseService<EventData>, IEventService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly TimeSpan EVENT_CACHE_DURATION = TimeSpan.FromMinutes(5);

        public EventService(
            ICrudRepository<EventData> repository,
            ICacheService<EventData> cacheService,
            IMongoIndexService<EventData> indexService,
            ILoggingService logger,
            IServiceScopeFactory scopeFactory
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            null,
            new[]
            {
                new CreateIndexModel<EventData>(Builders<EventData>.IndexKeys.Ascending(e => e.Name), new CreateIndexOptions { Name = "EventType_1" }),
                new CreateIndexModel<EventData>(Builders<EventData>.IndexKeys.Ascending(e => e.Processed), new CreateIndexOptions { Name = "Processed_1" }),
                new CreateIndexModel<EventData>(Builders<EventData>.IndexKeys.Ascending(e => e.ProcessedAt), new CreateIndexOptions { Name = "ProcessedAt_1" })
            }
        )
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        public async Task PublishAsync(BaseEvent eventToPublish)
        {
            using var Scope = Logger.BeginScope("EventService::PublishAsync", new Dictionary<string, object>
            {
                ["EventId"] = eventToPublish.EventId,
                ["EventType"] = eventToPublish.GetType().Name,
                ["Payload"] = eventToPublish.DomainEntityId.ToString(),
            });

            try
            {
                Logger.LogInformation($"Publishing {eventToPublish.GetType().Name} event");

                // Create and store event record
                var data = new EventData
                {
                    Name = eventToPublish.GetType().Name,
                    Payload = eventToPublish.DomainEntityId.ToString(),
                    CreatedAt = DateTime.UtcNow,
                    Processed = false
                };

                var insertResult = await InsertAsync(data);
                if (insertResult == null || !insertResult.IsSuccess)
                    await Logger.LogTraceAsync($"Failed to store event: {insertResult?.ErrorMessage ?? "Insert result returned null"}");

                eventToPublish.EventId = insertResult?.Data?.AffectedIds?.First() ?? Guid.Empty;

                // Resolve a new scoped mediator and publish
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Publish(eventToPublish);
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync($"Failed to publish event {eventToPublish.GetType().Name}: {ex.Message}", requiresResolution: true);
            }
        }

        public async Task<ResultWrapper<IEnumerable<EventData>>> GetUnprocessedEventsAsync(Type eventType, int limit = 100)
        {
            using var Scope = Logger.BeginScope("EventService::GetUnprocessedEventsAsync", new Dictionary<string, object>
            {
                ["EventType"] = eventType,
                ["Limit"] = limit,
            });
            try
            {
                var filter = Builders<EventData>.Filter.And(
                    Builders<EventData>.Filter.Eq(e => e.Name, eventType.Name),
                    Builders<EventData>.Filter.Eq(e => e.Processed, false)
                );

                var wrapper = await GetPaginatedAsync(filter, page: 1, pageSize: limit, sortField: nameof(EventData.CreatedAt), sortAscending: true);
                if (wrapper == null || !wrapper.IsSuccess)
                {
                    throw new EventFetchException($"Failed to fetch unprocessed events: {wrapper.ErrorMessage}");
                }
                return ResultWrapper<IEnumerable<EventData>>.Success(wrapper.Data.Items);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to get unprocessed events: {ex.Message}");
                return ResultWrapper<IEnumerable<EventData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> MarkAsProcessedAsync(Guid eventId)
        {
            using var Scope = Logger.BeginScope("EventService::MarkAsProcessedAsync", new Dictionary<string, object>
            {
                ["EventId"] = eventId,
            });
            try
            {
                var updateResult = await UpdateAsync(eventId, new { Processed = true, ProcessedAt = DateTime.UtcNow });
                if (updateResult == null || !updateResult.IsSuccess || updateResult.Data == null || !updateResult.Data.IsSuccess)
                    throw new Exception($"Failed to mark event {eventId} as processed: {updateResult?.ErrorMessage ?? "Update result returned null"}");
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync($"Mark as processed error: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }

        }

        public Task<ResultWrapper<IEnumerable<Domain.Models.Event.EventData>>> GetRecentEventsAsync(string eventType, int limit = 20)
            => SafeExecute(
                async () =>
                {
                    var filter = Builders<EventData>.Filter.Eq(e => e.Name, eventType);
                    var wrapper = await GetPaginatedAsync(filter, page: 1, pageSize: limit, sortField: nameof(EventData.CreatedAt), sortAscending: false);
                    return wrapper.Data.Items;
                }
            );
    }
}
