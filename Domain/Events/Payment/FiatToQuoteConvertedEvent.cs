using Domain.Models.Asset;
using Domain.Models.Payment;
using MediatR;

namespace Domain.Events.Payment
{
    // Event for MediatR
    public class FiatToQuoteConvertedEvent : BaseEvent, INotification
    {
        public PaymentData Payment { get; }
        public decimal AllocationQuantity { get;}
        public AssetData AllocationAsset { get;}
        public decimal QuoteOrderQuantity { get;}
        public FiatToQuoteConvertedEvent(PaymentData payment, decimal allocationQuantity, AssetData allocationAsset, decimal quoteOrderQuantity, IDictionary<string, object?> context) :
            base(context)
        {
            Payment = payment;
            AllocationQuantity = allocationQuantity;
            AllocationAsset = allocationAsset;
            QuoteOrderQuantity = quoteOrderQuantity;
            DomainEntityId = payment.Id;
        }
    }
}