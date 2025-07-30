namespace Application.Contracts.Requests.Withdrawal
{
    public class WithdrawalRequest
    {
        public Guid UserId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string WithdrawalMethod { get; set; } = string.Empty;
        public string WithdrawalAddress { get; set; } = string.Empty;
        public Dictionary<string, object>? AdditionalDetails { get; set; }
    }
}
