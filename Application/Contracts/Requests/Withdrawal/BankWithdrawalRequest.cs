namespace Application.Contracts.Requests.Withdrawal
{
    public class BankWithdrawalRequest
    {
        public Guid UserId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string WithdrawalMethod { get; set; } = string.Empty;
        public string WithdrawalAddress { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string AccountHolder { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public Dictionary<string, string>? AdditionalDetails { get; set; }
    }
}
