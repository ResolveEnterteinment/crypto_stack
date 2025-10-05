namespace Application.Contracts.Requests.KYC
{
    public class StatusUpdateRequest
    {
        public required string VerificationLevel { get; set; }
        public required string Status { get; set; }
        public string? Comment { get; set; }
    }
}
