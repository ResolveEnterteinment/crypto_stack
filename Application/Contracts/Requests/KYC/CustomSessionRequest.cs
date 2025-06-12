namespace Application.Contracts.Requests.KYC
{
    public class CustomSessionRequest
    {
        public Guid UserId { get; set; }
        public required string VerificationLevel { get; set; }
    }
}
