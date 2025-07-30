using Domain.Attributes;
using Domain.Constants.KYC;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Domain.Models.KYC
{
    /// <summary>
    /// KYC session data for tracking verification sessions
    /// </summary>
    [BsonCollection("kyc_sessions")]
    public class KycSessionData : BaseEntity
    {

        [BsonElement("userId")]
        [BsonRepresentation(BsonType.String)]
        [Required]
        public Guid UserId { get; set; }

        [BsonElement("sessionId")]
        [Required]
        public string SessionId { get; set; } = string.Empty;

        [BsonElement("status")]
        [Required]
        public string Status { get; set; } = "ACTIVE";

        [BsonElement("verificationLevel")]
        [Required]
        public string VerificationLevel { get; set; } = KycLevel.Standard;

        [BsonElement("securityContext")]
        [BsonIgnoreIfNull]
        public SessionSecurityContext? SecurityContext { get; set; }

        [BsonElement("progress")]
        public SessionProgress Progress { get; set; } = new();

        [BsonElement("expiresAt")]
        [Required]
        public DateTime ExpiresAt { get; set; }

        [BsonElement("completedAt")]
        [BsonIgnoreIfNull]
        public DateTime? CompletedAt { get; set; }
    }
}
