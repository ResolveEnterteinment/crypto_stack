namespace Domain.Constants.Treasury
{
    /// <summary>
    /// Treasury transaction types
    /// </summary>
    public static class TreasuryTransactionType
    {
        public const string Fee = "Fee";
        public const string Dust = "Dust";
        public const string Rounding = "Rounding";
        public const string Interest = "Interest";
        public const string Penalty = "Penalty";
        public const string Refund = "Refund";
        public const string Other = "Other";
    }

    /// <summary>
    /// Treasury transaction sources
    /// </summary>
    public static class TreasuryTransactionSource
    {
        // Fee Sources
        public const string PlatformFee = "PlatformFee";
        public const string TradingFee = "TradingFee";
        public const string WithdrawalFee = "WithdrawalFee";
        public const string SubscriptionFee = "SubscriptionFee";
        
        // Dust Sources
        public const string OrderDust = "OrderDust";
        public const string WithdrawalDust = "WithdrawalDust";
        public const string ConversionDust = "ConversionDust";
        
        // Rounding Sources
        public const string OrderRounding = "OrderRounding";
        public const string PriceRounding = "PriceRounding";
        public const string QuantityRounding = "QuantityRounding";
        
        // Other Sources
        public const string InterestEarned = "InterestEarned";
        public const string LateFee = "LateFee";
        public const string RefundClawback = "RefundClawback";
        public const string Other = "Other";
    }

    /// <summary>
    /// Treasury transaction status
    /// </summary>
    public static class TreasuryTransactionStatus
    {
        public const string Pending = "Pending";
        public const string Collected = "Collected";
        public const string Failed = "Failed";
        public const string Reversed = "Reversed";
        public const string UnderReview = "UnderReview";
    }

    /// <summary>
    /// Related entity types for treasury transactions
    /// </summary>
    public static class TreasuryRelatedEntityType
    {
        public const string Payment = "Payment";
        public const string Order = "Order";
        public const string Withdrawal = "Withdrawal";
        public const string Subscription = "Subscription";
        public const string Transaction = "Transaction";
    }
}
