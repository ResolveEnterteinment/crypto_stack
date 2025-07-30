using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Biometric verification data
    /// </summary>
    public class BiometricData
    {
        [BsonElement("faceImageHash")]
        public string? FaceImageHash { get; set; }

        [BsonElement("livenessScore")]
        public double? LivenessScore { get; set; }

        [BsonElement("faceMatchScore")]
        public double? FaceMatchScore { get; set; }

        [BsonElement("qualityScore")]
        public double? QualityScore { get; set; }

        [BsonElement("biometricTemplate")]
        [JsonIgnore] // Sensitive data
        public string? BiometricTemplate { get; set; }

        [BsonElement("verifiedAt")]
        public DateTime? VerifiedAt { get; set; }

        [BsonElement("deviceInfo")]
        [BsonIgnoreIfNull]
        public DeviceInfo? DeviceInfo { get; set; }
    }
}
