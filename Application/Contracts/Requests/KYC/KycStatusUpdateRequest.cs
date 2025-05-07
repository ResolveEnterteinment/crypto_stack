namespace Application.Contracts.Requests.KYC
{
    public class KycStatusUpdateRequest
    {
        public string Status { get; set; }
        public string Comment { get; set; }
    }
}
