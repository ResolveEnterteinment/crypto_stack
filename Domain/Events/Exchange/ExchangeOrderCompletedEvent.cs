using Domain.DTOs.Exchange;
using Domain.Models.Exchange;
using Domain.Models.Payment;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class ExchangeOrderCompletedEvent : BaseEvent, INotification
    {
        public ExchangeOrderData Order { get; }
        public ExchangeOrderCompletedEvent(ExchangeOrderData order, IDictionary<string, object?> context) :
            base(context)
        {
            Order = order;
            DomainEntityId = order.Id;
        }
    }
}