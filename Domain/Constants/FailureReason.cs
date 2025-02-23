namespace Domain.Constants
{
    public static class FailureReason
    {
        public const string ValidationError = "ValidationError";    // e.g., invalid quantity or allocation percentage
        public const string DataNotFound = "DataNotFound";          // Data not found in database
        public const string ExchangeApiError = "ExchangeApiError";  // Binance API failure
        public const string DatabaseError = "DatabaseError";        // MongoDB insertion failure
        public const string Unknown = "Unknown";                    // Catch-all for unexpected issues
    }
}
