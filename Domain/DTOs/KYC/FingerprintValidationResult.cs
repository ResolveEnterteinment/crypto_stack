namespace Domain.DTOs.KYC
{
    /// <summary>
    /// Result of device fingerprint validation
    /// </summary>
    public class FingerprintValidationResult
    {
        public bool IsValid { get; set; }
        public double TrustScore { get; set; }
        public List<string> ValidationErrors { get; set; } = new();
        public Dictionary<string, object> DeviceAttributes { get; set; } = new();
    }
}