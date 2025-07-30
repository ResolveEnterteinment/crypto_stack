using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Session security context
    /// </summary>
    public class SessionSecurityContext
    {
        [BsonElement("ipAddress")]
        public string IpAddress { get; set; } = string.Empty;

        [BsonElement("userAgent")]
        public string UserAgent { get; set; } = string.Empty;

        [BsonElement("deviceFingerprint")]
        public string? DeviceFingerprint { get; set; }

        [BsonElement("geolocation")]
        [BsonIgnoreIfNull]
        public GeoLocation? GeoLocation { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updatedAt")]
        public DateTime UpdateddAt { get; set; } = DateTime.UtcNow;

        [BsonElement("lastAccessedAt")]
        public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("securityFlags")]
        public List<string> SecurityFlags { get; set; } = new();
    }
}
