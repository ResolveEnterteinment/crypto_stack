// Domain/DTOs/Settings/OnfidoSettings.cs
namespace Domain.DTOs.Settings
{
    public class SumSubSettings
    {
        public string ApiUrl { get; set; } = "https://api.sumsub.com";
        public string AppToken { get; set; }
        public string SecretKey { get; set; }
        public string LevelNameBasic { get; set; } = "basic-kyc-level";
        public string LevelNameStandard { get; set; } = "standard-kyc-level";
        public string LevelNameAdvanced { get; set; } = "advanced-kyc-level";
        public string LevelNameEnhanced { get; set; } = "enhanced-kyc-level";
        public string WebhookSecret { get; set; }
        public string[] AllowedReferrers { get; set; }
    }
}