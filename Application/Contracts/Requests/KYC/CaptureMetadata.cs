using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.KYC
{
    /// <summary>
    /// Metadata about the capture session
    /// </summary>
    public class CaptureMetadata
    {
        /// <summary>
        /// Device fingerprint for security validation
        /// </summary>
        [Required]
        public string DeviceFingerprint { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp of the capture (Unix timestamp in milliseconds)
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// User agent string
        /// </summary>
        public string? UserAgent { get; set; }

        /// <summary>
        /// Screen resolution during capture
        /// </summary>
        public string? ScreenResolution { get; set; }

        /// <summary>
        /// Camera specifications
        /// </summary>
        public Dictionary<string, object>? CameraInfo { get; set; }

        /// <summary>
        /// Additional capture environment data
        /// </summary>
        public Dictionary<string, object>? EnvironmentData { get; set; }
    }
}