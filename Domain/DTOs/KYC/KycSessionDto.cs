namespace Domain.DTOs.KYC
{
    public class KycSessionDto
    {
        public string SessionId { get; set; }
        public string VerificationUrl { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string Status { get; set; }
    }
}
