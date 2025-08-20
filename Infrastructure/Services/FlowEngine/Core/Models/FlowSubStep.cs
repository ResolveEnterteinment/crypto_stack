namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowSubStep
    {
        public string Name { get; set; }
        public Func<FlowContext, Task<StepResult>> ExecuteAsync { get; set; }
        public int Priority { get; set; } = 0;
        public object SourceData { get; set; }
        public int Index { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public TimeSpan? EstimatedDuration { get; set; }
        public string ResourceGroup { get; set; } // For round-robin distribution
    }
}
