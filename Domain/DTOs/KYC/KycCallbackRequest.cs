namespace Domain.DTOs.KYC
{
    public class KycCallbackRequest
    {
        public string ReferenceId { get; set; }
        public string SessionId { get; set; }
        public string Status { get; set; }
        public Dictionary<string, object> VerificationResult { get; set; }
    }
}
