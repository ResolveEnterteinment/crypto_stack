using Domain.DTOs.Payment;

namespace Domain.DTOs.Dashboard
{
    public class SubscriptionPaymentStatusDto
    {
        public Guid SubscriptionId { get; set; }
        public string Status { get; set; }
        public int FailedPaymentCount { get; set; }
        public PaymentDto? LatestPayment { get; set; }
    }
}
