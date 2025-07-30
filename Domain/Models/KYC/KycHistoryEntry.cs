
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Domain.Models.KYC
{
    /// <summary>
    /// KYC history entry for audit trail
    /// </summary>
    public class KycHistoryEntry
    {
        [BsonElement("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [BsonElement("action")]
        [Required]
        public string Action { get; set; } = string.Empty;

        [BsonElement("previousStatus")]
        public string? PreviousStatus { get; set; }

        [BsonElement("newStatus")]
        public string? NewStatus { get; set; }

        [BsonElement("session")]
        public Guid? Session { get; set; }

        [BsonElement("performedBy")]
        [Required]
        public string PerformedBy { get; set; } = string.Empty;

        [BsonElement("reason")]
        public string? Reason { get; set; }

        [BsonElement("details")]
        public Dictionary<string, object>? Details { get; set; }

        [BsonElement("ipAddress")]
        public string? IpAddress { get; set; }

        [BsonElement("userAgent")]
        public string? UserAgent { get; set; }
    }
}