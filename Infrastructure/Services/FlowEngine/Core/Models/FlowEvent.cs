using Infrastructure.Utilities;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public Guid FlowId { get; set; }
        public string EventType { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, SafeObject> Data { get; set; } = new();
    }
}
