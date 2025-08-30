using MongoDB.Bson.Serialization.Attributes;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    [BsonIgnoreExtraElements]
    public class FlowBranch
    {
        [BsonIgnore]
        public Func<FlowContext, bool> Condition { get; set; }
        public bool IsDefault { get; set; } = false;
        public List<FlowSubStep> Steps { get; set; } = new();
    }
}
