namespace Domain.DTOs.KYC
{
    public class KycVerificationRequest
    {
        public Guid UserId { get; set; }
        public Guid SessionId { get; set; }
        public string VerificationLevel { get; set; } = "STANDARD";
        public Dictionary<string, object> Data { get; set; } = [];
    }
}