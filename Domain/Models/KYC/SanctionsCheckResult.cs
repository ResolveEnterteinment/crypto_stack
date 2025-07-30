using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// Sanctions check result
    /// </summary>
    public class SanctionsCheckResult
    {
        [BsonElement("isSanctioned")]
        public bool IsSanctioned { get; set; }

        [BsonElement("sanctionsList")]
        public List<string> SanctionsList { get; set; } = new();

        [BsonElement("matches")]
        public List<SanctionMatch> Matches { get; set; } = new();

        [BsonElement("checkedAt")]
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("dataSource")]
        public string DataSource { get; set; } = "INTERNAL";
    }

}
