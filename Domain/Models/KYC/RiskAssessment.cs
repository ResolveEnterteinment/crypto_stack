using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Risk assessment results
    /// </summary>
    public class RiskAssessment
    {
        [BsonElement("overallScore")]
        public double OverallScore { get; set; }

        [BsonElement("riskLevel")]
        public string RiskLevel { get; set; } = "LOW"; // LOW, MEDIUM, HIGH

        [BsonElement("riskFactors")]
        public List<RiskFactor> RiskFactors { get; set; } = new();

        [BsonElement("amlFlags")]
        public List<string> AmlFlags { get; set; } = new();

        [BsonElement("pepCheck")]
        [BsonIgnoreIfNull]
        public PepCheckResult? PepCheck { get; set; }

        [BsonElement("sanctionsCheck")]
        [BsonIgnoreIfNull]
        public SanctionsCheckResult? SanctionsCheck { get; set; }

        [BsonElement("assessedAt")]
        public DateTime AssessedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("assessedBy")]
        public string AssessedBy { get; set; } = "SYSTEM";
    }
}
