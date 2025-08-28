namespace Domain.DTOs.Subscription
{
    /// <summary>
    /// Enhanced allocation data transfer object with comprehensive asset details
    /// </summary>
    public class EnhancedAllocationDto
    {
        /// <summary>
        /// The unique identifier of the asset
        /// </summary>
        public Guid AssetId { get; set; }

        /// <summary>
        /// The human-readable name of the asset
        /// </summary>
        public string AssetName { get; set; } = string.Empty;

        /// <summary>
        /// The trading ticker symbol
        /// </summary>
        public string AssetTicker { get; set; } = string.Empty;

        /// <summary>
        /// The asset symbol
        /// </summary>
        public string AssetSymbol { get; set; } = string.Empty;

        /// <summary>
        /// The type of asset (e.g., "crypto", "stock", "etf")
        /// </summary>
        public string AssetType { get; set; } = string.Empty;

        /// <summary>
        /// The asset class (e.g., "equity", "commodity", "currency")
        /// </summary>
        public string AssetClass { get; set; } = string.Empty;

        /// <summary>
        /// The exchange where this asset is traded
        /// </summary>
        public string Exchange { get; set; } = string.Empty;

        /// <summary>
        /// The decimal precision for this asset
        /// </summary>
        public uint Precision { get; set; }

        /// <summary>
        /// The subunit name (e.g., "satoshi" for Bitcoin)
        /// </summary>
        public string? SubunitName { get; set; }

        /// <summary>
        /// The percentage allocation for this asset
        /// </summary>
        public decimal PercentAmount { get; set; }

        /// <summary>
        /// The calculated allocation amount based on subscription amount and percentage
        /// </summary>
        public decimal AllocationAmount { get; set; }

        public int Priority { get; set; } = 0;

        /// <summary>
        /// The currency of the allocation amount
        /// </summary>
        public string Currency { get; set; } = string.Empty;

        /// <summary>
        /// Current price of the asset (optional, for performance data)
        /// </summary>
        public decimal? CurrentPrice { get; set; }

        /// <summary>
        /// 24-hour price change percentage (optional, for performance data)
        /// </summary>
        public decimal? PriceChange24h { get; set; }

        /// <summary>
        /// When the price data was last updated
        /// </summary>
        public DateTime? LastUpdated { get; set; }
    }
}