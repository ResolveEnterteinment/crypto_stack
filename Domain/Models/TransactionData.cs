namespace Domain.Models
{
    public class TransactionData : BaseEntity
    {
        public required string UserId { get; set; }
        public required string PaymentProviderId { get; set; }
        public required float TotalAmount { get; set; }
        public required float PaymentProviderFee { get; set; }
        public required float PlatformFee { get; set; }
        public required float NetAmount { get; set; }
        public required string Status { get; set; }
    }
}
