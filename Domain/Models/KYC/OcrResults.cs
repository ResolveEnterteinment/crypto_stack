using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// OCR (Optical Character Recognition) results
    /// </summary>
    public class OcrResults
    {
        [BsonElement("extractedText")]
        public string ExtractedText { get; set; } = string.Empty;

        [BsonElement("confidence")]
        public double Confidence { get; set; }

        [BsonElement("fields")]
        public Dictionary<string, OcrField> Fields { get; set; } = new();

        [BsonElement("processedAt")]
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("engine")]
        public string Engine { get; set; } = "TESSERACT";
    }
}
