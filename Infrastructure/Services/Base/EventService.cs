using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Base;
using Domain.DTOs.Logging;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Event;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Infrastructure.Services.Base
{
    public class EventService : IEventService
    {
        private readonly ICrudRepository<EventData> _repository;
        private readonly IResilienceService<EventData> _resilienceService;
        private readonly ICacheService<EventData> _cacheService;
        private readonly IMongoIndexService<EventData> _indexService;
        private readonly ILoggingService _loggingService;
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly TimeSpan EVENT_CACHE_DURATION = TimeSpan.FromMinutes(5);
        private static readonly IReadOnlySet<string> _validPropertyNames;

        public EventService(
            ICrudRepository<EventData> repository,
            ICacheService<EventData> cacheService,
            IMongoIndexService<EventData> indexService,
            ILoggingService loggingService,
            IResilienceService<EventData> resilienceService,
            IServiceScopeFactory scopeFactory,
            IEnumerable<CreateIndexModel<EventData>>? indexModels = null
        )
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

            if (indexModels != null)
            {
                _indexService.EnsureIndexesAsync(indexModels);
            }
        }

        public async Task PublishAsync(BaseEvent eventToPublish)
        {
            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Event",
                    FileName = "EventService",
                    OperationName = "PublishAsync(BaseEvent eventToPublish)",
                    State = {
                        ["EventId"] = eventToPublish.EventId,
                        ["EventType"] = eventToPublish.GetType().Name,
                        ["Payload"] = eventToPublish.DomainEntityId.ToString(),
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    _loggingService.LogInformation($"Publishing {eventToPublish.GetType().Name} event");

                    // Create and store event record
                    var data = new EventData
                    {
                        Id = eventToPublish.EventId == Guid.Empty ? Guid.NewGuid() : eventToPublish.EventId,
                        Name = eventToPublish.GetType().Name,
                        Payload = eventToPublish.DomainEntityId.ToString()
                    };

                    var insertResult = await _repository.InsertAsync(data);
                    if (insertResult == null || !insertResult.IsSuccess)
                    {
                        await _loggingService.LogTraceAsync($"Failed to store event: {insertResult?.ErrorMessage ?? "Insert result returned null"}");
                    }

                    eventToPublish.EventId = insertResult?.AffectedIds?.FirstOrDefault() ?? Guid.Empty;

                    // Resolve a new scoped mediator and publish
                    using var scope = _scopeFactory.CreateScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                    var publishTask = mediator.Publish(eventToPublish);
                    return Task.CompletedTask;
                })
                .WithQuickOperationResilience(TimeSpan.FromSeconds(3))
                .OnError(async ex =>
                {
                    await MarkAsFailedAsync(eventToPublish.EventId, ex.Message); // Ensure we mark the event as processed even if publishing fails
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<PaginatedResult<EventData>>> GetUnprocessedEventsAsync(Type eventType, int limit = 100)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Event",
                    FileName = "EventService",
                    OperationName = "GetUnprocessedEventsAsync(Type eventType, int limit = 100)",
                    State = {
                        ["EventType"] = eventType.Name
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<EventData>.Filter.And([
                        Builders<EventData>.Filter.Eq(e => e.Name, eventType.Name),
                        Builders<EventData>.Filter.Eq(e => e.Processed, false)
                    ]);

                    var sort = Builders<EventData>.Sort.Ascending(e => e.CreatedAt);

                    var paginatedData = await _repository.GetPaginatedAsync(filter, page: 1, pageSize: limit, sortDefinition: sort);
                    if (paginatedData == null)
                    {
                        throw new EventFetchException($"Failed to fetch unprocessed events: Paginated data returned null");
                    }
                    return paginatedData;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> MarkAsProcessedAsync(Guid eventId)
        => await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Event",
                    FileName = "EventService",
                    OperationName = "MarkAsProcessedAsync(Guid eventId)",
                    State = {
                        ["EventId"] = eventId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var updateResult = await _repository.UpdateAsync(eventId, new { Processed = true, ProcessedAt = DateTime.UtcNow });
                    if (updateResult == null || !updateResult.IsSuccess)
                        throw new Exception($"Failed to mark event {eventId} as processed: {updateResult?.ErrorMessage ?? "Update result returned null"}");
                    return true;
                })
                .ExecuteAsync();

        public Task<ResultWrapper<bool>> MarkAsFailedAsync(Guid eventId, string errorMessage)
        => _resilienceService.CreateBuilder(
            new Scope
            {
                NameSpace = "Infrastructure.Services.Event",
                FileName = "EventService",
                OperationName = "MarkAsFailedAsync(Guid eventId)",
                State = {
                    ["EventId"] = eventId,
                },
                LogLevel = LogLevel.Error
            },
            async () =>
            {
                var updateResult = await _repository.UpdateAsync(eventId, 
                    new {
                        ErrorMessage = errorMessage,
                        LastAttempt = DateTime.UtcNow
                    });
                if (updateResult == null || !updateResult.IsSuccess)
                    throw new Exception($"Failed to mark event {eventId} as failed: {updateResult?.ErrorMessage ?? "Update result returned null"}");
                return true;
            })
            .ExecuteAsync();

        public Task<ResultWrapper<PaginatedResult<EventData>>> GetRecentEventsAsync(string eventType, int limit = 20)
        => _resilienceService.CreateBuilder(
            new Scope
            {
                NameSpace = "Infrastructure.Services.Event",
                FileName = "EventService",
                OperationName = "GetRecentEventsAsync(string eventType, int limit = 20)",
                State = {
                    ["EventType"] = eventType,
                },
                LogLevel = LogLevel.Error
            },
            async () =>
            {
                var filter = Builders<EventData>.Filter.Eq(e => e.Name, eventType);
                var sort = Builders<EventData>.Sort.Descending(e => e.CreatedAt);
                var paginatedData = await _repository.GetPaginatedAsync(filter, page: 1, pageSize: limit, sortDefinition: sort);
                return paginatedData;
            })
            .ExecuteAsync();
    }
}