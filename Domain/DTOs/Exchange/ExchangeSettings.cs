namespace Domain.DTOs.Exchange
{
    public class ExchangeSettings
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public bool IsTestnet { get; set; }
        public string ReserveStableAssetTicker { get; set; }
    }
}
