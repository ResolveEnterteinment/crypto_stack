using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Document verification results
    /// </summary>
    public class DocumentVerificationResult
    {
        [BsonElement("isValid")]
        public bool IsValid { get; set; }

        [BsonElement("confidenceScore")]
        public double ConfidenceScore { get; set; }

        [BsonElement("tamperDetected")]
        public bool TamperDetected { get; set; }

        [BsonElement("expirationValid")]
        public bool ExpirationValid { get; set; }

        [BsonElement("qualityScore")]
        public double QualityScore { get; set; }

        [BsonElement("ocrResults")]
        [BsonIgnoreIfNull]
        public OcrResults? OcrResults { get; set; }

        [BsonElement("verifiedAt")]
        public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("verificationMethod")]
        public string VerificationMethod { get; set; } = "AUTOMATED";

        [BsonElement("failureReasons")]
        public List<string> FailureReasons { get; set; } = new();
    }
}
