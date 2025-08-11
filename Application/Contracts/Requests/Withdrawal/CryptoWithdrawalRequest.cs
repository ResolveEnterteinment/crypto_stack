namespace Application.Contracts.Requests.Withdrawal
{
    public class CryptoWithdrawalRequest
    {
        public Guid UserId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string WithdrawalMethod { get; set; } = string.Empty;
        public string WithdrawalAddress { get; set; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public string Memo { get; set; } = string.Empty;
    }
}
