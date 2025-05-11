// Domain/DTOs/Settings/OnfidoSettings.cs
namespace Domain.DTOs.Settings
{
    public class SumSubSettings
    {
        public string ApiUrl { get; set; };
        public string AppToken { get; set; }
        public string SecretKey { get; set; }
        public string LevelNameBasic { get; set; }
        public string LevelNameStandard { get; set; }
        public string LevelNameAdvanced { get; set; }
        public string LevelNameEnhanced { get; set; }
        public string WebhookSecret { get; set; }
        public string[] AllowedReferrers { get; set; }
    }
}