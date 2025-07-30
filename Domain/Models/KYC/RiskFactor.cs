using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Risk factor information
    /// </summary>
    public class RiskFactor
    {
        [BsonElement("type")]
        public string Type { get; set; } = string.Empty;

        [BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        [BsonElement("severity")]
        public string Severity { get; set; } = "LOW"; // LOW, MEDIUM, HIGH, CRITICAL

        [BsonElement("score")]
        public double Score { get; set; }

        [BsonElement("detectedAt")]
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("source")]
        public string Source { get; set; } = "SYSTEM";
    }
}
