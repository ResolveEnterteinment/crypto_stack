namespace Domain.DTOs.Balance
{
    /// <summary>
    /// Simplified balance info for an asset
    /// </summary>
    public class AssetBalance
    {
        public string Ticker { get; set; } = string.Empty;
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public decimal Total { get; set; }
        public decimal? ValueInUSD { get; set; }
    }
}
