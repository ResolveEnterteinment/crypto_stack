// Domain/DTOs/Withdrawal/WithdrawalDto.cs
namespace Domain.DTOs.Withdrawal
{
    public class WithdrawalRequest
    {
        public Guid UserId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string WithdrawalMethod { get; set; }
        public string WithdrawalAddress { get; set; }
        public Dictionary<string, object> AdditionalDetails { get; set; } = new Dictionary<string, object>();
    }
}