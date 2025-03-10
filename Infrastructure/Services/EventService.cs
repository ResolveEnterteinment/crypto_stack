using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Events;
using Domain.Models.Event;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class EventService : BaseService<EventData>, IEventService
    {
        protected readonly IMediator _mediator;
        public EventService(
            IMediator mediator,
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<BalanceService> logger) : base(mongoClient, mongoDbSettings, "events", logger)
        {
            _mediator = mediator;
        }

        public Task Publish(BaseEvent eventType)
        {
            _logger.LogInformation($"Publishing {eventType.GetType().Name} event with id {eventType.EventId}");
            return _mediator.Publish(eventType);
        }
    }
}