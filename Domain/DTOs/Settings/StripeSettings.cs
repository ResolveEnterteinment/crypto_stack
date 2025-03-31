namespace Domain.DTOs.Settings
{
    public class StripeSettings
    {
        public required string ApiSecret { get; set; }
        public required string ApiKey { get; set; }
        public required string WebhookSecret { get; set; }
    }
}
