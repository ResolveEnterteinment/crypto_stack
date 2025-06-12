namespace Domain.DTOs.Settings
{
    public class KycSettings
    {
        /// <summary>
        /// API key for OpenSanctions API access
        /// </summary>
        public string OpenSanctionsApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Secret key for validating webhook signatures
        /// </summary>
        public string WebhookSecret { get; set; } = string.Empty;

        /// <summary>
        /// Directory to store downloaded sanctions data
        /// </summary>
        public string SanctionsDataDirectory { get; set; } = "SanctionsData";
        public required string OpenSanctionsEndpoint { get; set; }

        /// <summary>
        /// Base URL for verification process
        /// </summary>
        public string VerificationBaseUrl { get; set; } = "/minimal-kyc";

        /// <summary>
        /// How often to update sanctions data (in hours)
        /// </summary>
        public int SanctionsUpdateIntervalHours { get; set; } = 24;
    }
}