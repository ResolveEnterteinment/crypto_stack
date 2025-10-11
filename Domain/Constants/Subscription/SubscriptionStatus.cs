namespace Domain.Constants.Subscription
{
    /// <summary>
    /// Comprehensive categorization of failure reasons
    /// </summary>

    public class SubscriptionStatus
    {
        public const string Pending = "PENDING";
        public const string Active = "ACTIVE";
        public const string Paused = "PAUSED";
        public const string Canceled = "CANCELED";
        public const string Deleted = "DELETED";
        public const string Suspended = "SUSPENDED";

        public static readonly IReadOnlyCollection<string> AllValues = new[]
        {
            Pending,
            Active,
            Paused,
            Canceled,
            Deleted,
            Suspended
        };
    }
}