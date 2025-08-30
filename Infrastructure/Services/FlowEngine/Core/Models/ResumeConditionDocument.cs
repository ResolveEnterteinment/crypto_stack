using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class ResumeConditionDocument
    {
        [BsonId]
        public Guid Id { get; set; }

        [BsonElement("flowId")]
        public Guid FlowId { get; set; }

        [BsonElement("checkInterval")]
        [BsonTimeSpanOptions(BsonType.String)]
        public TimeSpan CheckInterval { get; set; }

        [BsonElement("nextCheck")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime NextCheck { get; set; }

        [BsonElement("maxRetries")]
        public int MaxRetries { get; set; }

        [BsonElement("currentRetries")]
        public int CurrentRetries { get; set; }

        [BsonElement("createdAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; }
    }
}
