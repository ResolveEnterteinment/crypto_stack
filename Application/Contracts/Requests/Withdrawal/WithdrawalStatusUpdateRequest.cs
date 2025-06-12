namespace Application.Contracts.Requests.Withdrawal
{
    public class WithdrawalStatusUpdateRequest
    {
        public string Status { get; set; }
        public string? Comment { get; set; }
        public string? TransactionHash { get; set; }
    }
}
