namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Health information for the FlowEngine
    /// </summary>
    public class FlowEngineHealth
    {
        public int RunningFlowsCount { get; set; }
        public int PausedFlowsCount { get; set; }
        public int RecentFailuresCount { get; set; }
        public bool IsHealthy { get; set; }
        public DateTime CheckedAt { get; set; }
        public string Status { get; set; } = "Healthy";
        public Dictionary<string, object> AdditionalInfo { get; set; } = new();
    }
}