using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Verification result details
    /// </summary>
    public class VerificationResult
    {
        [BsonElement("userId")]
        [BsonRepresentation(BsonType.String)]
        public Guid UserId { get; set; }

        [BsonElement("verificationLevel")]
        public string VerificationLevel { get; set; } = string.Empty;

        [BsonElement("status")]
        public string Status { get; set; } = string.Empty;

        [BsonElement("processedAt")]
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("checks")]
        public List<VerificationCheck> Checks { get; set; } = new();

        [BsonElement("failureReasons")]
        public List<string> FailureReasons { get; set; } = new();

        [BsonElement("processingTimeMs")]
        public long ProcessingTimeMs { get; set; }
    }

    /// <summary>
    /// Individual verification check result
    /// </summary>
    public class VerificationCheck
    {
        [BsonElement("checkType")]
        public string CheckType { get; set; } = string.Empty;

        [BsonElement("checkName")]
        public string CheckName { get; set; } = string.Empty;

        [BsonElement("passed")]
        public bool Passed { get; set; }

        [BsonElement("score")]
        public double Score { get; set; }

        [BsonElement("processedAt")]
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("failureReason")]
        public string? FailureReason { get; set; }

        [BsonElement("details")]
        public Dictionary<string, object> Details { get; set; } = new();
    }
}
