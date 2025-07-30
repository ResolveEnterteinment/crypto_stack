using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Models.KYC
{
    /// <summary>
    /// PEP (Politically Exposed Person) check result
    /// </summary>
    public class PepCheckResult
    {
        [BsonElement("isPep")]
        public bool IsPep { get; set; }

        [BsonElement("pepCategory")]
        public string? PepCategory { get; set; }

        [BsonElement("matches")]
        public List<PepMatch> Matches { get; set; } = new();

        [BsonElement("checkedAt")]
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("dataSource")]
        public string DataSource { get; set; } = "INTERNAL";
    }
}
