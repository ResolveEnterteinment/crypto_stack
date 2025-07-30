using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Individual OCR field result
    /// </summary>
    public class OcrField
    {
        [BsonElement("value")]
        public string Value { get; set; } = string.Empty;

        [BsonElement("confidence")]
        public double Confidence { get; set; }

        [BsonElement("boundingBox")]
        [BsonIgnoreIfNull]
        public BoundingBox? BoundingBox { get; set; }
    }
}
