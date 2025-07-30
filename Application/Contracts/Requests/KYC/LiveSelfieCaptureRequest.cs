using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.KYC
{
    /// <summary>
    /// Request model for live document capture verification
    /// </summary>
    public class LiveSelfieCaptureRequest
    {
        /// <summary>
        /// The KYC session ID
        /// </summary>
        [Required]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Base64 encoded image data from live capture
        /// </summary>
        [Required]
        public ImageCaptureDto ImageData { get; set; }

        public bool IsLive { get; set; } = false;

        /// <summary>
        /// Metadata about the capture session
        /// </summary>
        [Required]
        public CaptureMetadata CaptureMetadata { get; set; } = new();
    }
}