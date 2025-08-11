namespace Domain.DTOs.Withdrawal
{
    public class WithdrawalReceiptDto
    {
        public Guid Id { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public string WithdrawalMethod { get; set; }
        public string WithdrawalAddress { get; set; }
    }
}
