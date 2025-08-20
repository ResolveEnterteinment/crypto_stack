using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowStep
    {
        public string Name { get; set; }
        public Func<FlowContext, Task<StepResult>> ExecuteAsync { get; set; }
        public Func<FlowContext, bool> Condition { get; set; }
        public List<string> Dependencies { get; set; } = new();
        public bool CanRunInParallel { get; set; }
        public int MaxRetries { get; set; } = 0;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan? Timeout { get; set; }
        public List<Type> Middleware { get; set; } = new();
        public List<FlowBranch> Branches { get; set; } = new();
        public DynamicBranchingConfig DynamicBranching { get; set; }
        public Type TriggeredFlow { get; set; }
        public bool IsCritical { get; set; }

        // NEW: Pause/Resume capabilities
        public Func<FlowContext, PauseCondition> PauseCondition { get; set; }
        public ResumeConfig ResumeConfig { get; set; }
    }
}
