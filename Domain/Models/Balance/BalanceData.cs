namespace Domain.Models.Balance
{
    public class BalanceData : BaseEntity
    {
        public Guid UserId { get; set; }
        public Guid AssetId { get; set; }
        public decimal Available { get; set; } = decimal.Zero;
        public decimal Locked { get; set; } = decimal.Zero;
    }
}
