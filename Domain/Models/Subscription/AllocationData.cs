namespace Domain.Models.Subscription
{
    public class AllocationData
    {
        public required Guid AssetId { get; set; }
        public string Ticker { get; set; }
        public required int PercentAmount { get; set; } // 0-100
    }
}
