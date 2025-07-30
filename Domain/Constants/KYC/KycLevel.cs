namespace Domain.Constants.KYC
{
    public static class KycLevel
    {
        public const string None = "NONE";
        public const string Basic = "BASIC";
        public const string Standard = "STANDARD";
        public const string Advanced = "ADVANCED";
        public const string Enhanced = "ENHANCED";

        public static readonly string[] AllValues = {
            None, Basic, Standard, Advanced, Enhanced
        };
    }
}
