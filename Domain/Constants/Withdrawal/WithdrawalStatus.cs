namespace Domain.Constants.Withdrawal
{
    public static class WithdrawalStatus
    {
        public const string Pending = "PENDING";
        public const string Approved = "APPROVED";
        public const string Completed = "COMPLETED";
        public const string Rejected = "REJECTED";
        public const string Failed = "FAILED";
        public const string Cancelled = "CANCELLED";

        public static readonly List<string> AllValues =
        [
            Pending, Approved, Completed, Rejected, Failed, Cancelled
        ];
    }
}
