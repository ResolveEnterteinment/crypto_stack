namespace Application.Contracts.Requests.Withdrawal
{
    public class CanUserWithdrawRequest
    {
        public decimal Amount { get; set; }
        public string Ticker { get; set; } = string.Empty;
    }
}
