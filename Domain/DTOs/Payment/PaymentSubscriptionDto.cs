namespace Domain.DTOs.Payment
{
    public class PaymentSubscriptionDto
    {
        public DateTime NextDueDate { get; set; }
        public string CustomerId { get; set; }
        public string DefaultPaymentMethod { get; set; }
    }
}
