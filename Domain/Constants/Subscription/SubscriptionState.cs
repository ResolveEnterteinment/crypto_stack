namespace Domain.Constants.Subscription
{
    /// <summary>
    /// Comprehensive categorization of failure reasons
    /// </summary>

    public class SubscriptionState
    {
        public const string Idle = "IDLE";
        public const string PendingCheckout = "PENDING_CHECKOUT";
        public const string PendingPayment = "PENDING_PAYMENT";
        public const string ProcessingInvoice = "PROCESSING_INVOICE";
        public const string AcquiringAssets = "ACQUIRING_ASSETS";

        public static readonly IReadOnlyCollection<string> AllValues = new[]
        {
            Idle, PendingCheckout, PendingPayment, ProcessingInvoice, AcquiringAssets
        };
    }
}