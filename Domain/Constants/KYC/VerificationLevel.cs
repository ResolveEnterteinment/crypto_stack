namespace Domain.Constants.KYC
{
    public class VerificationLevel
    {
        public const string Basic = "BASIC";
        public const string Standard = "STANDARD";
        public const string Advanced = "ADVANCED";
        public const string Enhanced = "ENHANCED";

        public static readonly string[] AllValues = {
            Basic, Standard, Advanced, Enhanced
        };

        public static int GetIndex(string level)
        {
            return level switch
            {
                Basic => 1,
                Standard => 2,
                Advanced => 3,
                Enhanced => 4,
                _ => 0 // Return -1 for invalid levels
            };
        }
    }
}
