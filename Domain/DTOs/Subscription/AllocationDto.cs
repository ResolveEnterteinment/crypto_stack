namespace Domain.DTOs.Subscription
{
    public class AllocationDto
    {
        public Guid AssetId { get; set; }
        public string AssetName { get; set; }
        public string AssetTicker { get; set; }
        public decimal PercentAmount { get; set; }
    }
}
