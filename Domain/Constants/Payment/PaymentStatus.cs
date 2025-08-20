using MongoDB.Driver;

namespace Domain.Constants.Payment
{
    public static class PaymentStatus
    {
        // Standard payment statuses
        public const string Pending = "PENDING";
        public const string Processing = "PROCESSING";
        public const string Completed = "COMPLETED";
        public const string Failed = "FAILED";
        public const string Refunded = "REFUNDED";
        // Enhanced flow statuses
        public const string Queued = "QUEUED";
        public const string Cancelled = "CANCELLED";
        public const string Paid = "PAID";
        // Updated statses after exchange order
        public const string Filled = "FILLED";
        public const string PartiallyFilled = "PARTIALLY_FILLED";

        public static string[] AllValues()
        {
            return [
                
                Pending, Processing, Completed, Failed, Refunded,
                Queued, Cancelled, Paid,
                Filled, PartiallyFilled];
        }
    }
}
