// Domain/Models/KYC/KycData.cs
using Domain.Attributes;
using Domain.Constants.KYC;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Main KYC data entity storing user verification information
    /// </summary>
    [BsonCollection("kycs")]
    public class KycData : BaseEntity
    {
        [BsonElement("userId")]
        [BsonRepresentation(BsonType.String)]
        [Required]
        public Guid UserId { get; set; }

        [BsonElement("status")]
        [Required]
        public string Status { get; set; } = KycStatus.NotStarted;

        [BsonElement("verificationLevel")]
        [Required]
        public string VerificationLevel { get; set; } = KycLevel.None;

        [BsonElement("encryptedPersonalData")]
        public string? EncryptedPersonalData { get; set; }

        [BsonElement("personalData")]
        [BsonIgnoreIfNull]
        [JsonIgnore] // Don't serialize in API responses
        public Dictionary<string, object>? PersonalData { get; set; }

        [BsonElement("verificationResults")]
        [BsonIgnoreIfNull]
        public VerificationResult? VerificationResults { get; set; }

        [BsonElement("documents")]
        public List<Guid> Documents { get; set; } = new();
        [BsonElement("liveCaptures")]
        public List<Guid> LiveCaptures { get; set; } = new();

        [BsonElement("biometricData")]
        [BsonIgnoreIfNull]
        public BiometricData? BiometricData { get; set; }

        [BsonElement("riskAssessment")]
        [BsonIgnoreIfNull]
        public RiskAssessment? RiskAssessment { get; set; }

        [BsonElement("amlStatus")]
        public string? AmlStatus { get; set; }

        [BsonElement("amlCheckDate")]
        [BsonIgnoreIfNull]
        public DateTime? AmlCheckDate { get; set; }

        [BsonElement("amlRiskScore")]
        public double? AmlRiskScore { get; set; }

        [BsonElement("securityFlags")]
        [BsonIgnoreIfNull]
        public KycSecurityFlags? SecurityFlags { get; set; }

        [BsonElement("history")]
        public List<KycHistoryEntry> History { get; set; } = new();

        [BsonElement("verifiedAt")]
        [BsonIgnoreIfNull]
        public DateTime? VerifiedAt { get; set; }

        [BsonElement("expiresAt")]
        [BsonIgnoreIfNull]
        public DateTime? ExpiresAt { get; set; }

        [BsonElement("version")]
        public int Version { get; set; } = 1;

        [BsonElement("metadata")]
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}