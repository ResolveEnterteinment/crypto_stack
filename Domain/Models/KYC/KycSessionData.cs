namespace Domain.Models.KYC
{
    public class KycSessionData : BaseEntity
    {
        public required Guid UserId { get; set; }
        public required string VerificationLevel { get; set; }
        public DateTime ExpiresAt { get; set; }
        public required string Status { get; set; }
    }
}
