using Domain.Events.Payment;
using MediatR;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowEventService : INotificationHandler<CheckoutSessionCompletedEvent>
    {
        Task PublishAsync(string eventType, object eventData, string correlationId = null);
        void SubscribeAsync(string eventType, Func<object, Task> handler);
        Task ProcessEventAsync(string eventType, object eventData);
    }
}
