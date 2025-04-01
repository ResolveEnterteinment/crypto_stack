using Domain.Models.Crypto;

namespace Domain.DTOs.Balance
{
    public class BalanceDto
    {
        public string AssetId { get; set; }
        public string AssetName { get; set; }
        public string Ticker { get; set; }
        public string Symbol { get; set; }
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public decimal Total { get; set; }
        public AssetData AssetDocs { get; set; }
    }
}
