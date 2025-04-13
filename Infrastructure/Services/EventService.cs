// Improved and refactored EventService
using Application.Interfaces;
using Domain.DTOs.Settings;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Event;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;

namespace Infrastructure.Services
{
    public class EventService : BaseService<EventData>, IEventService
    {
        private readonly IMediator _mediator;
        private static readonly TimeSpan EVENT_CACHE_DURATION = TimeSpan.FromMinutes(5);

        public EventService(
            IMediator mediator,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<EventService> logger,
            IMemoryCache cache
        ) : base(
            mongoClient,
            mongoDbSettings,
            "events",
            logger,
            cache,
            new List<CreateIndexModel<EventData>>
            {
                new CreateIndexModel<EventData>(
                    Builders<EventData>.IndexKeys.Ascending(e => e.Name),
                    new CreateIndexOptions { Name = "EventType_1" }
                ),
                new CreateIndexModel<EventData>(
                    Builders<EventData>.IndexKeys.Ascending(e => e.Processed),
                    new CreateIndexOptions { Name = "Processed_1" }
                ),
                new CreateIndexModel<EventData>(
                    Builders<EventData>.IndexKeys.Ascending(e => e.ProcessedAt),
                    new CreateIndexOptions { Name = "ProcessedAt_1" }
                )
            }
        )
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        }

        /// <summary>
        /// Publishes a domain event to the MediatR pipeline
        /// </summary>
        /// <param name="eventToPublish">The event to publish</param>
        /// <returns>A task that completes when the event is published</returns>
        public async Task Publish(BaseEvent eventToPublish)
        {
            try
            {
                _logger.LogInformation(
                    "Publishing {EventType} event with id {EventId}",
                    eventToPublish.GetType().Name,
                    eventToPublish.EventId);

                var storedEvent = new EventData
                {
                    Name = eventToPublish.GetType().Name,
                    Payload = eventToPublish.DomainRecordId.ToString()
                };

                // Apply retry pattern for storing event data
                var retryPolicy = Polly.Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (ex, time, retryCount, ctx) =>
                        {
                            _logger.LogWarning(ex, "Attempt {RetryCount} to store {EventType} event failed. Retrying in {RetryTime}ms",
                                retryCount, eventToPublish.GetType().Name, time.TotalMilliseconds);
                        });

                var storedEventResult = await retryPolicy.ExecuteAsync(async () =>
                    await InsertOneAsync(storedEvent));

                if (storedEventResult == null || !storedEventResult.IsAcknowledged || storedEventResult.InsertedId is null)
                {
                    throw new DatabaseException($"Failed to store {eventToPublish.GetType().Name} event: {storedEventResult?.ErrorMessage ?? "Insert result returned null."}");
                }

                eventToPublish.EventId = (Guid)storedEventResult.InsertedId;

                await _mediator.Publish(eventToPublish);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to publish event: {EventId} of type {EventType}",
                    eventToPublish.EventId,
                    eventToPublish.GetType().Name);

                // We don't rethrow here to avoid disrupting the calling process
                // Events should be handled with resilience
            }
        }

        /// <summary>
        /// Gets unprocessed events of a specific type
        /// </summary>
        /// <param name="eventType">The event type name</param>
        /// <param name="limit">Maximum number of events to retrieve</param>
        /// <returns>List of unprocessed events</returns>
        public async Task<IEnumerable<EventData>> GetUnprocessedEventsAsync(Type eventType, int limit = 100)
        {
            try
            {
                // Don't cache unprocessed events queries - we need fresh data
                var filter = Builders<EventData>.Filter.And(
                    Builders<EventData>.Filter.Eq(e => e.GetType(), eventType),
                    Builders<EventData>.Filter.Eq(e => e.Processed, false)
                );

                var sort = Builders<EventData>.Sort.Ascending(e => e.CreatedAt);

                return await _collection.Find(filter)
                    .Sort(sort)
                    .Limit(limit)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get unprocessed events of type {EventType}", eventType);
                throw;
            }
        }

        /// <summary>
        /// Marks an event as processed
        /// </summary>
        /// <param name="eventId">The event ID</param>
        /// <returns>True if successful</returns>
        public async Task<bool> MarkAsProcessedAsync(Guid eventId)
        {
            try
            {
                var update = Builders<EventData>.Update
                    .Set(e => e.Processed, true)
                    .Set(e => e.ProcessedAt, DateTime.UtcNow);

                var result = await UpdateOneAsync(eventId, new
                {
                    Processed = true,
                    ProcessedAt = DateTime.UtcNow
                });

                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark event {EventId} as processed", eventId);
                return false;
            }
        }

        /// <summary>
        /// Gets recent events of a specific type
        /// </summary>
        /// <param name="eventType">The event type name</param>
        /// <param name="limit">Maximum number of events to retrieve</param>
        /// <returns>List of recent events</returns>
        public async Task<IEnumerable<EventData>> GetRecentEventsAsync(string eventType, int limit = 20)
        {
            try
            {
                // Use caching for recent events queries
                string cacheKey = $"events:recent:{eventType}:{limit}";

                return await GetOrCreateCachedItemAsync(
                    cacheKey,
                    async () =>
                    {
                        var filter = Builders<EventData>.Filter.Eq(e => e.GetType().Name, eventType);
                        var sort = Builders<EventData>.Sort.Descending(e => e.CreatedAt);

                        return await _collection.Find(filter)
                            .Sort(sort)
                            .Limit(limit)
                            .ToListAsync();
                    },
                    EVENT_CACHE_DURATION);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get recent events of type {EventType}", eventType);
                throw;
            }
        }
    }
}