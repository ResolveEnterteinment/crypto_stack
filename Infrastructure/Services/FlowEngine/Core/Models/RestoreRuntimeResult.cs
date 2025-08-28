namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class RestoreRuntimeResult
    {
        public int TotalFlowsChecked { get; set; }
        public int FlowsRestored { get; set; }
        public int FlowsFailed { get; set; }
        public List<string> RestoredFlowIds { get; set; } = new();
        public List<string> FailedFlowIds { get; set; } = new();
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan Duration { get; set; }
    }
}
