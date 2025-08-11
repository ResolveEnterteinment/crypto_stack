namespace Application.Contracts.Requests.KYC
{
    public class StatusUpdateRequest
    {
        public required string Status { get; set; }
        public string? Comment { get; set; }
    }
}
