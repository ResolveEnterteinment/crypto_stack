namespace Domain.DTOs
{
    public class StripeSettings
    {
        public required string ApiSecret { get; set; }
        public required string ApiKey { get; set; }
    }
}
