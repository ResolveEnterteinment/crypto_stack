using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// PEP match information
    /// </summary>
    public class PepMatch
    {
        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("position")]
        public string Position { get; set; } = string.Empty;

        [BsonElement("country")]
        public string Country { get; set; } = string.Empty;

        [BsonElement("matchScore")]
        public double MatchScore { get; set; }

        [BsonElement("lastUpdated")]
        public DateTime LastUpdated { get; set; }
    }
}
