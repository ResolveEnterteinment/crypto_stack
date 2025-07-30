using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Sanction match information
    /// </summary>
    public class SanctionMatch
    {
        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("listName")]
        public string ListName { get; set; } = string.Empty;

        [BsonElement("reason")]
        public string Reason { get; set; } = string.Empty;

        [BsonElement("matchScore")]
        public double MatchScore { get; set; }

        [BsonElement("dateAdded")]
        public DateTime DateAdded { get; set; }
    }
}
