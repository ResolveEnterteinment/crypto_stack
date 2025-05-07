// Domain/DTOs/Settings/OnfidoSettings.cs
namespace Domain.DTOs.Settings
{
    public class OnfidoSettings
    {
        public string ApiUrl { get; set; } = "https://api.onfido.com/v3/";
        public string SdkUrl { get; set; } = "https://id.onfido.com/";
        public string ApiKey { get; set; }
        public string[] AllowedReferrers { get; set; } = new[] { "*.yourdomain.com" };
        public string WebhookToken { get; set; }
    }
}