namespace Domain.DTOs.Payment
{
    /// <summary>
    /// Data transfer object for Stripe subscription details
    /// </summary>
    public class StripeSubscriptionDetails
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public DateTime CurrentPeriodStart { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
        public DateTime? CancelAt { get; set; }
        public DateTime? CanceledAt { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Interval { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
