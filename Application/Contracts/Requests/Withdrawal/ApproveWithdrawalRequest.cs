namespace Application.Contracts.Requests.Withdrawal
{
    public class ApproveWithdrawalRequest
    {
        public string? Comment { get; set; }
        public string? TransactionHash { get; set; }
    }
}
