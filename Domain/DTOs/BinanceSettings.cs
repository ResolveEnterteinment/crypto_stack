namespace Domain.DTOs
{
    public class BinanceSettings
    {
        public required string ApiKey { get; set; }
        public required string ApiSecret { get; set; }
        public required bool IsTestnet { get; set; }
    }
}
