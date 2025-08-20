namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Result of flow recovery operation
    /// </summary>
    public class RecoveryResult
    {
        public int TotalFlowsChecked { get; set; }
        public int FlowsRecovered { get; set; }
        public int FlowsFailed { get; set; }
        public List<string> RecoveredFlowIds { get; set; } = new();
        public List<string> FailedFlowIds { get; set; } = new();
        public TimeSpan Duration { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }
}