using Application.Interfaces;
using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Event;
using Infrastructure.Services.Base;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class EventService : BaseService<EventData>, IEventService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly TimeSpan EVENT_CACHE_DURATION = TimeSpan.FromMinutes(5);

        public EventService(
            ICrudRepository<EventData> repository,
            ICacheService<EventData> cacheService,
            IMongoIndexService<EventData> indexService,
            ILogger<EventService> logger,
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

        public async Task Publish(BaseEvent eventToPublish)
        {
            try
            {
                Logger.LogInformation("Publishing {EventType} event", eventToPublish.GetType().Name);

                // Create and store event record
                var data = new EventData
                {
                    Name = eventToPublish.GetType().Name,
                    Payload = eventToPublish.DomainEntityId.ToString(),
                    CreatedAt = DateTime.UtcNow,
                    Processed = false
                };

                var insertResult = await InsertAsync(data);
                if (!insertResult.IsSuccess)
                    throw new DatabaseException($"Failed to store event: {insertResult.ErrorMessage}");

                eventToPublish.EventId = data.Id;

                // Resolve a new scoped mediator and publish
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Publish(eventToPublish);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to publish event {EventType}", eventToPublish.GetType().Name);
            }
        }

        public async Task<IEnumerable<EventData>> GetUnprocessedEventsAsync(Type eventType, int limit = 100)
        {
            var filter = Builders<EventData>.Filter.And(
                Builders<EventData>.Filter.Eq(e => e.Name, eventType.Name),
                Builders<EventData>.Filter.Eq(e => e.Processed, false)
            );

            var wrapper = await GetPaginatedAsync(filter, page: 1, pageSize: limit, sortField: nameof(EventData.CreatedAt), sortAscending: true);
            return wrapper.IsSuccess ? wrapper.Data.Items : Array.Empty<EventData>();
        }

        public async Task<bool> MarkAsProcessedAsync(Guid eventId)
        {
            var updateResult = await UpdateAsync(eventId, new { Processed = true, ProcessedAt = DateTime.UtcNow });
            return updateResult.IsSuccess;
        }

        public Task<ResultWrapper<IEnumerable<EventData>>> GetRecentEventsAsync(string eventType, int limit = 20)
            => FetchCached(
                $"events:recent:{eventType}:{limit}",
                async () =>
                {
                    var filter = Builders<EventData>.Filter.Eq(e => e.Name, eventType);
                    var wrapper = await GetPaginatedAsync(filter, page: 1, pageSize: limit, sortField: nameof(EventData.CreatedAt), sortAscending: false);
                    return wrapper.Data.Items;
                },
                EVENT_CACHE_DURATION,
                () => new KeyNotFoundException("Recent events not found.")
            );
    }
}
