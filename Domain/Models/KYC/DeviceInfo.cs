using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Device information for biometric capture
    /// </summary>
    public class DeviceInfo
    {
        [BsonElement("deviceType")]
        public string DeviceType { get; set; } = string.Empty;

        [BsonElement("operatingSystem")]
        public string OperatingSystem { get; set; } = string.Empty;

        [BsonElement("browser")]
        public string Browser { get; set; } = string.Empty;

        [BsonElement("cameraResolution")]
        public string? CameraResolution { get; set; }

        [BsonElement("deviceFingerprint")]
        public string? DeviceFingerprint { get; set; }

        [BsonElement("trustLevel")]
        public string TrustLevel { get; set; } = "UNKNOWN"; // TRUSTED, UNKNOWN, SUSPICIOUS
    }

}
