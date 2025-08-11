using Domain.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Audit log for KYC operations
    /// </summary>
    [BsonCollection("kyc_audit_logs")]
    public class KycAuditLogData : BaseEntity
    {
        [BsonElement("userId")]
        [BsonRepresentation(BsonType.String)]
        [Required]
        public Guid UserId { get; set; }

        [BsonElement("action")]
        [Required]
        public string Action { get; set; } = string.Empty;

        [BsonElement("details")]
        [Required]
        public string Details { get; set; } = string.Empty;

        [BsonElement("ipAddress")]
        public string? IpAddress { get; set; }

        [BsonElement("userAgent")]
        public string? UserAgent { get; set; }

        [BsonElement("performedBy")]
        public string? PerformedBy { get; set; }

        [BsonElement("timestamp")]
        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [BsonElement("severity")]
        public string Severity { get; set; } = "INFO";

        [BsonElement("category")]
        public string Category { get; set; } = "KYC";

        [BsonElement("additionalData")]
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }
}
