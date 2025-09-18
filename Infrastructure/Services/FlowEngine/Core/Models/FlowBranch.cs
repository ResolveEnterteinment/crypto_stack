using MongoDB.Bson.Serialization.Attributes;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    [BsonIgnoreExtraElements]
    public class FlowBranch
    {
        public string Name { get; set; }
        [BsonIgnore]
        public Func<FlowExecutionContext, bool>? Condition { get; set; } = null;
        public bool IsDefault { get; set; } = false;
        public object? SourceData { get; set; } = null;
        public int Priority { get; set; } = 0;
        public string? ResourceGroup { get; set; } = null; // For round-robin distribution
        public List<FlowSubStep> Steps { get; set; } = new();
    }
}
