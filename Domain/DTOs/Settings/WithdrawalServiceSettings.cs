namespace Domain.DTOs.Settings
{
    public class WithdrawalServiceSettings
    {
        public decimal MinimumWithdrawalValue { get; set; } = 15;
        public string MinimumWithdrawalTicker { get; set; } = "USD";
    }
}
