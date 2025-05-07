namespace Domain.DTOs.Withdrawal
{
    public class WithdrawalRequestDto
    {
        public Guid Id { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string WithdrawalMethod { get; set; }
        public string WithdrawalAddress { get; set; }
        public string TransactionHash { get; set; }
    }
}
