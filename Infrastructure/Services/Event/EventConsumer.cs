using Application.Interfaces.Logging;
using Domain.Events;
using MediatR;

namespace Infrastructure.Services.Event
{
    public abstract class EventConsumer<TEvent> : INotificationHandler<TEvent>
        where TEvent : BaseEvent, INotification
    {
        private readonly ILoggingService _logger;

        protected EventConsumer(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task Handle(TEvent notification, CancellationToken cancellationToken)
        {
            using (_logger.EnrichScope(notification.Context))
            {
                await HandleEventAsync(notification, cancellationToken);
            }
        }

        /// <summary>
        /// Implement your actual event handling logic here.
        /// Context will already be enriched.
        /// </summary>
        protected abstract Task HandleEventAsync(TEvent notification, CancellationToken cancellationToken);
    }
}
