using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Document security checks
    /// </summary>
    public class DocumentSecurityChecks
    {
        [BsonElement("malwareDetected")]
        public bool MalwareDetected { get; set; }

        [BsonElement("fileIntegrityValid")]
        public bool FileIntegrityValid { get; set; }

        [BsonElement("metadataClean")]
        public bool MetadataClean { get; set; }

        [BsonElement("suspiciousPatterns")]
        public List<string> SuspiciousPatterns { get; set; } = new();

        [BsonElement("scanResults")]
        public Dictionary<string, object> ScanResults { get; set; } = new();

        [BsonElement("scannedAt")]
        public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    }

}
