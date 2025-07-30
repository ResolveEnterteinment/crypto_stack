using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Security flags for KYC records
    /// </summary>
    public class KycSecurityFlags
    {
        [BsonElement("requiresReview")]
        public bool RequiresReview { get; set; }

        [BsonElement("isHighRisk")]
        public bool IsHighRisk { get; set; }

        [BsonElement("isPoliticallyExposed")]
        public bool IsPoliticallyExposed { get; set; }

        [BsonElement("isSanctioned")]
        public bool IsSanctioned { get; set; }

        [BsonElement("failureReasons")]
        public List<string>? FailureReasons { get; set; }

        [BsonElement("highRiskIndicators")]
        public List<string>? HighRiskIndicators { get; set; }

        [BsonElement("fraudIndicators")]
        public List<string>? FraudIndicators { get; set; }

        [BsonElement("blockedAt")]
        [BsonIgnoreIfNull]
        public DateTime? BlockedAt { get; set; }

        [BsonElement("blockedBy")]
        public string? BlockedBy { get; set; }

        [BsonElement("blockReason")]
        public string? BlockReason { get; set; }
    }
}
