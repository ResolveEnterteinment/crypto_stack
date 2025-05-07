namespace Domain.DTOs.Withdrawal
{
    public class WithdrawalLimitDto
    {
        public string KycLevel { get; set; }
        public decimal DailyLimit { get; set; }
        public decimal MonthlyLimit { get; set; }
        public decimal DailyRemaining { get; set; }
        public decimal MonthlyRemaining { get; set; }
        public decimal DailyUsed { get; set; }
        public decimal MonthlyUsed { get; set; }
        public DateTime PeriodResetDate { get; set; } // When the monthly limit resets
    }
}
