namespace Domain.Constants.KYC
{
    public static class KycLevel
    {
        public const string None = "NONE";
        public const string Basic = "BASIC";      // Email + Basic Info
        public const string Standard = "STANDARD"; // ID Verification
        public const string Advanced = "ADVANCED"; // ID + Proof of Address + Face Verification
        public const string Enhanced = "ENHANCED"; // Enhanced Due Diligence for high-value customers
    }
}
