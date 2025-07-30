// Domain/Constants/WithdrawalLimits.cs
namespace Domain.Constants.Withdrawal
{
    public static class WithdrawalLimits
    {
        // Unverified users (no KYC)
        public const decimal NONE_DAILY_LIMIT = 0; // No withdrawals allowed
        public const decimal NONE_MONTHLY_LIMIT = 0;

        // Basic KYC (email verification + basic info, no documents)
        public const decimal BASIC_DAILY_LIMIT = 100;
        public const decimal BASIC_MONTHLY_LIMIT = 3000;

        // Standard KYC (ID verification)
        public const decimal STANDARD_DAILY_LIMIT = 50000;
        public const decimal STANDARD_MONTHLY_LIMIT = 500000;

        // Advanced KYC (ID + proof of address + face verification)
        public const decimal ADVANCED_DAILY_LIMIT = 200000;
        public const decimal ADVANCED_MONTHLY_LIMIT = 2000000;

        // Enhanced KYC (full due diligence)
        public const decimal ENHANCED_DAILY_LIMIT = 1000000;
        public const decimal ENHANCED_MONTHLY_LIMIT = 10000000;
    }
}