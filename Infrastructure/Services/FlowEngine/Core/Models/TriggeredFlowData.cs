using Infrastructure.Utilities;
using MongoDB.Bson.Serialization.Attributes;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class TriggeredFlowData
    {
        [BsonElement("type")]
        public string Type { get; set; }

        [BsonElement("flowId")]
        public Guid? FlowId { get; set; }
        [BsonElement("triggeredByStep")]
        public string? TiggeredByStep { get; set; } = null;

        [BsonElement("data")]
        public Dictionary<string, SafeObject> Data = [];

        [BsonIgnore]
        public Func<FlowExecutionContext, Dictionary<string, object>>? InitialDataFactory { get; private set; }

        public TriggeredFlowData() { }
        public TriggeredFlowData(string type, Func<FlowExecutionContext, Dictionary<string, object>>? initialDataFactory = null)
        {
            Type = type;
            InitialDataFactory = initialDataFactory;
        }
    }
}