namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Statistical information about flows over a time period
    /// </summary>
    public class FlowEngineStatistics
    {
        public int TotalFlows { get; set; }
        public int CompletedFlows { get; set; }
        public int FailedFlows { get; set; }
        public int RunningFlows { get; set; }
        public int PausedFlows { get; set; }
        public int CancelledFlows { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan Period { get; set; }
        public Dictionary<string, int> FlowsByType { get; set; } = new();
        public Dictionary<string, int> FailuresByReason { get; set; } = new();
        public double AverageExecutionTime { get; set; }
    }
}
