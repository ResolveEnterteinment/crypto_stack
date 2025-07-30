using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Extracted data from documents
    /// </summary>
    public class DocumentExtractedData
    {
        [BsonElement("documentNumber")]
        public string? DocumentNumber { get; set; }

        [BsonElement("firstName")]
        public string? FirstName { get; set; }

        [BsonElement("lastName")]
        public string? LastName { get; set; }

        [BsonElement("dateOfBirth")]
        [BsonIgnoreIfNull]
        public DateTime? DateOfBirth { get; set; }

        [BsonElement("expirationDate")]
        [BsonIgnoreIfNull]
        public DateTime? ExpirationDate { get; set; }

        [BsonElement("issuingCountry")]
        public string? IssuingCountry { get; set; }

        [BsonElement("nationality")]
        public string? Nationality { get; set; }

        [BsonElement("gender")]
        public string? Gender { get; set; }

        [BsonElement("address")]
        public string? Address { get; set; }

        [BsonElement("extractedAt")]
        public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("extractionConfidence")]
        public double ExtractionConfidence { get; set; }
    }
}
