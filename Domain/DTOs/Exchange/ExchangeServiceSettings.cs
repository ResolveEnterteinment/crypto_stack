namespace Domain.DTOs.Exchange
{
    public class ExchangeServiceSettings
    {
        public IDictionary<string, ExchangeSettings> ExchangeSettings { get; set; } = new Dictionary<string, ExchangeSettings>();
        public string PlatformFiatAssetId { get; set; }

    }
}