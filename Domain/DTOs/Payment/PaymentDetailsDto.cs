// Domain/DTOs/Payment/PaymentDetailsDto.cs
using Domain.Models.Payment;

namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Data transfer object for payment details
    /// </summary>
    public class PaymentDetailsDto
    {
        /// <summary>
        /// The payment ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The ID of the user who made the payment
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// The ID of the subscription this payment is for
        /// </summary>
        public string SubscriptionId { get; set; }

        /// <summary>
        /// The payment provider (e.g., Stripe)
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// The provider-specific payment ID
        /// </summary>
        public string PaymentProviderId { get; set; }

        /// <summary>
        /// The total payment amount
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// The fee charged by the payment provider
        /// </summary>
        public decimal PaymentProviderFee { get; set; }

        /// <summary>
        /// The fee charged by the platform
        /// </summary>
        public decimal PlatformFee { get; set; }

        /// <summary>
        /// The net amount after fees
        /// </summary>
        public decimal NetAmount { get; set; }

        /// <summary>
        /// The currency code
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// The current status of the payment
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// When the payment was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        public PaymentDetailsDto()
        {
        }
        public PaymentDetailsDto(PaymentData payment)
        {
            Id = payment.Id.ToString();
            UserId = payment.UserId.ToString();
            SubscriptionId = payment.SubscriptionId.ToString();
            Provider = payment.Provider;
            PaymentProviderId = payment.PaymentProviderId;
            TotalAmount = payment.TotalAmount;
            PaymentProviderFee = payment.PaymentProviderFee;
            PlatformFee = payment.PlatformFee;
            NetAmount = payment.NetAmount;
            Currency = payment.Currency;
            Status = payment.Status;
            CreatedAt = payment.CreatedAt;
        }
    }
}