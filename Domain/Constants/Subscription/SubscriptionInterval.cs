namespace Domain.Constants.Subscription
{
    public class SubscriptionInterval
    {
        public const string OneTime = "ONE-TIME";
        public const string Daily = "DAILY";
        public const string Weekly = "WEEKLY";
        public const string Monthly = "MONTHLY";
        public const string Yearly = "YEARLY";

        public static readonly List<string> AllValues = new List<string>()
        {
            OneTime, Daily, Weekly, Monthly, Yearly
        };
    }
}