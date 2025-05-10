namespace Domain.DTOs.Settings
{
    public class StripeSettings
    {
        public required string ApiSecret { get; set; }
        public required string ApiKey { get; set; }
        public required string WebhookSecret { get; set; }
        public string PaymentUpdateSuccessUrl { get; set; } = "https://localhost:5173/payment/update/success";
        public string PaymentUpdateCancelUrl { get; set; } = "https://localhost:5173/payment/update/cancel";
    }
}
