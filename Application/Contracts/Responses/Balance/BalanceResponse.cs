using Domain.DTOs.Asset;

namespace Application.Contracts.Responses.Balance
{
    public class BalanceResponse : BaseResponse
    {
        public required Guid Id { get; set; }
        public decimal Available { get; set; }
        public decimal Locked { get; set; }
        public decimal Total { get; set; }
        public required AssetDto Asset { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
