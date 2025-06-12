namespace Application.Contracts.Requests.KYC
{
    public class CustomVerificationRequest
    {
        public Guid UserId { get; set; }
        public required string SessionId { get; set; }
        public required string DocumentImage { get; set; }
        public required string SelfieImage { get; set; }
        public required Dictionary<string, object> PersonalInfo { get; set; }
        public bool DocumentVerified { get; set; }
        public double FaceMatchConfidence { get; set; }
    }
}
