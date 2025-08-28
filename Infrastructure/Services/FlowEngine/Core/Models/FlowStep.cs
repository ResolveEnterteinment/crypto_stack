using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowStep
    {
        public string Name { get; set; } = "Unnamed Step";
        public StepStatus Status { get; set; } = StepStatus.Pending;
        public Func<FlowContext, Task<StepResult>>? ExecuteAsync { get; set; }
        public List<string> StepDependencies { get; set; } = [];
        public Dictionary<string, Type> DataDependencies { get; internal set; } = [];
        public Func<FlowContext, bool>? Condition { get; set; }
        public StepResult? Result {  get; set; }
        public bool CanRunInParallel { get; set; }
        public int MaxRetries { get; set; } = 0;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan? Timeout { get; set; }
        public List<Type> Middleware { get; set; } = [];
        public List<FlowBranch> Branches { get; set; } = [];
        public DynamicBranchingConfig? DynamicBranching { get; set; }
        public List<Type> TriggeredFlows { get; set; } = [];
        public string? JumpTo { get; set; } = null;
        public bool IsCritical { get; set; } = false;
        public bool AllowFailure { get; set; } = false;
        public bool IsIdempotent { get; set; } = false;

        // NEW: Pause/Resume capabilities
        public Func<FlowContext, PauseCondition>? PauseCondition { get; set; }
        public ResumeConfig? ResumeConfig { get; set; }

        //Sub step related properties
        public int Priority { get; set; } = 0;
        public object? SourceData { get; set; }
        public int Index { get; set; } = -1;
        public Dictionary<string, object> Metadata { get; set; } = [];
        public TimeSpan? EstimatedDuration { get; set; }
        public string? ResourceGroup { get; set; } // For round-robin distribution
    }
}
