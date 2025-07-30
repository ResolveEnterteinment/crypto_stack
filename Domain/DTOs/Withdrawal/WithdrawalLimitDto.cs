using Domain.Constants.KYC;

namespace Domain.DTOs.Withdrawal
{
    public class WithdrawalLimitDto
    {
        public string KycLevel { get; set; } = Domain.Constants.KYC.KycLevel.None;
        public string Currency { get; set; } = "USD";
        public decimal DailyLimit { get; set; }
        public decimal MonthlyLimit { get; set; }
        public decimal DailyRemaining { get; set; }
        public decimal MonthlyRemaining { get; set; }
        public decimal DailyUsed { get; set; }
        public decimal MonthlyUsed { get; set; }
        public DateTime PeriodResetDate { get; set; } // When the monthly limit resets
    }
}
