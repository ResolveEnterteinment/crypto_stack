using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Geographic location information
    /// </summary>
    public class GeoLocation
    {
        [BsonElement("latitude")]
        public double? Latitude { get; set; }

        [BsonElement("longitude")]
        public double? Longitude { get; set; }

        [BsonElement("country")]
        public string? Country { get; set; }

        [BsonElement("region")]
        public string? Region { get; set; }

        [BsonElement("city")]
        public string? City { get; set; }

        [BsonElement("accuracy")]
        public double? Accuracy { get; set; }

        [BsonElement("determinedAt")]
        public DateTime DeterminedAt { get; set; } = DateTime.UtcNow;
    }
}
