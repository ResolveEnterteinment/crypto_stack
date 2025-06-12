namespace Domain.Constants.KYC
{
    public static class KycStatus
    {
        public const string NotStarted = "NOT_STARTED";
        public const string InProgress = "IN_PROGRESS";
        public const string Pending = "PENDING_VERIFICATION";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
        public const string NeedsReview = "NEEDS_REVIEW";
        public const string AdditionalInfoRequired = "ADDITIONAL_INFO_REQUIRED";
        public const string Expired = "EXPIRED";

        public static string[] AllValues = [
            NotStarted,
            InProgress,
            Pending,
            Approved,
            Rejected,
            NeedsReview,
            AdditionalInfoRequired,
            Expired
            ];
    }
}
