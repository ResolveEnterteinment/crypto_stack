using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.KYC
{
    /// <summary>
    /// Request model for live document capture verification
    /// </summary>
    public class LiveDocumentCaptureRequest
    {
        /// <summary>
        /// The KYC session ID
        /// </summary>
        [Required]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Type of document being captured
        /// </summary>
        [Required]
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>
        /// Base64 encoded image data from live capture
        /// </summary>
        [Required]
        public ImageCaptureDto[] ImageData { get; set; } = [];

        public bool IsLive { get; set; } = false;

        // Duplex support
        public bool IsDuplex { get; set; }
        /// <summary>
        /// Metadata about the capture session
        /// </summary>
        [Required]
        public CaptureMetadata CaptureMetadata { get; set; } = new();
    }

    public class ImageCaptureDto
    {
        public string Side { get; set; } = string.Empty; // e.g., "front", "back", "left", "right"
        public string ImageData { get; set; } = string.Empty;
        public bool IsLive { get; set; } = false;
        [Range(0, 1)]
        public double ConfidenceScore { get; set; } = 0.0;
        /// <summary>
        /// Quality score of the captured image (0-100)
        /// </summary>
        [Range(0, 100)]
        public int QualityScore { get; set; }
    }
}