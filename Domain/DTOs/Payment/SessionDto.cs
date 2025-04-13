namespace Domain.DTOs.Payment
{
    public class SessionDto
    {
        public string Id { get; set; }
        public string Provider { get; set; }
        public string ClientSecret { get; set; }
        public string Url { get; set; }
        public string SubscriptionId { get; set; }
        public string InvoiceId { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public string Status { get; set; }
    }
}
