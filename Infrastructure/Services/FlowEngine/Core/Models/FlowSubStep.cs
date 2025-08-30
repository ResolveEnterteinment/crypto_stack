using Infrastructure.Utilities;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowSubStep : FlowStep
    {
        public int Priority { get; set; } = 0;
        public object SourceData { get; set; } = null;
        public int Index { get; set; } = -1;
        public Dictionary<string, SafeObject> Metadata { get; set; } = new();
        public TimeSpan? EstimatedDuration { get; set; }
        public string? ResourceGroup { get; set; } // For round-robin distribution
    }
}
