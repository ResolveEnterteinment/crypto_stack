using Domain.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Record for live captured documents with enhanced security metadata
    /// </summary>
    [BsonCollection("live_captures")]
    public class LiveCaptureRecord: BaseEntity
    {
        public Guid UserId { get; set; }
        public Guid SessionId { get; set; }
        
        [Required]
        public string DocumentType { get; set; } = string.Empty;
        
        [Required]
        public string SecureFilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }

        [Required]
        public string FileHash { get; set; } = string.Empty;
        public bool IsDuplex { get; set; }
        public string? BackSideFilePath { get; set; } = string.Empty;
        public long BackSideFileSize { get; set; }
        public string? BackSideFileHash { get; set; } = string.Empty;
        
        [Required]
        public string DeviceFingerprint { get; set; } = string.Empty;
        
        public DateTimeOffset CaptureTimestamp { get; set; }
        public DateTime ProcessedAt { get; set; }
        
        [Required]
        public string Status { get; set; } = string.Empty;

        [Required]
        public bool IsEncrypted { get; set; } = false;
        public string EncryptionMethod { get; set; } = string.Empty;
    }
}