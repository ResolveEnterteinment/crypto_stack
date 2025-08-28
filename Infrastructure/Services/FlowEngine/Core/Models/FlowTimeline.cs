using Infrastructure.Services.FlowEngine.Core.Enums;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Timeline of flow execution events
    /// </summary>
    public class FlowTimeline
    {
        public Guid FlowId { get; set; }
        public List<FlowTimelineEvent> Events { get; set; } = new();
    }

    /// <summary>
    /// Single event in flow timeline
    /// </summary>
    public class FlowTimelineEvent
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; }
        public string StepName { get; set; }
        public FlowStatus Status { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }
}