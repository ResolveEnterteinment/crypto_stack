using Domain.DTOs.Asset;
using Domain.Models.Balance;

namespace Domain.DTOs.Balance
{
    public class BalanceDto
    {
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public decimal Total { get; set; }
        public AssetDto? Asset { get; set; }
        public DateTime UpdatedAt { get; set; }

        public BalanceDto(BalanceData balance)
        {
            Available = balance.Available;
            Locked = balance.Locked;
            Total = balance.Total;
            Asset = new AssetDto(balance.Asset!);
            UpdatedAt = balance.UpdatedAt ?? DateTime.Now;
        }
    }
}
