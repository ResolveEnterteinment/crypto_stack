namespace Domain.Constants.KYC
{
    public static class KycStatus
    {
        public const string NotStarted = "NOT_STARTED";
        public const string Pending = "PENDING";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
        public const string NeedsReview = "NEEDS_REVIEW";
        public const string Expired = "EXPIRED";
        public const string Blocked = "BLOCKED";

        public static readonly string[] AllValues = {
            NotStarted, Pending, Approved, Rejected, NeedsReview, Expired, Blocked
        };
    }
}
