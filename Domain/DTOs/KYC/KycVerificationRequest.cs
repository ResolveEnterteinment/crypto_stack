// Domain/DTOs/KYC/KycVerificationRequest.cs
namespace Domain.DTOs.KYC
{
    public class KycVerificationRequest
    {
        public Guid UserId { get; set; }
        public string VerificationLevel { get; set; } = "STANDARD";
        public bool RedirectAfterVerification { get; set; } = true;
        public string RedirectUrl { get; set; }
        public string Locale { get; set; } = "en";
        public Dictionary<string, object> UserData { get; set; } = new Dictionary<string, object>();
    }
}