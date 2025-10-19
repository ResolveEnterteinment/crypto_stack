namespace Domain.DTOs.Settings
{
    public class PaymentServiceSettings
    {
        public required string DefaultProvider { get; set; }
        public required decimal PlatformFeePercent { get; set; }
    }
}
