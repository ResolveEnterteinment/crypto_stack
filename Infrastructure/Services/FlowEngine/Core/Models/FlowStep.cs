using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using MongoDB.Bson.Serialization.Attributes;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    [BsonIgnoreExtraElements]
    public class FlowStep
    {
        public string Name { get; set; } = "Unnamed Step";
        public StepStatus Status { get; set; } = StepStatus.Pending;
        [BsonIgnore]
        public Func<FlowExecutionContext, Task<StepResult>>? ExecuteAsync { get; set; }
        public List<string> StepDependencies { get; set; } = [];
        public Dictionary<string, Type> DataDependencies { get; internal set; } = [];
        [BsonIgnore]
        public Func<FlowExecutionContext, bool>? Condition { get; set; }
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
        public int? MaxJumps { get; set; } = null;
        public int CurrentJumps { get; set; } = 0;
        public bool IsCritical { get; set; } = false;
        public bool AllowFailure { get; set; } = false;
        public bool IsIdempotent { get; set; } = false;

        // NEW: Pause/Resume capabilities
        [BsonIgnore]
        public Func<FlowExecutionContext, PauseCondition>? PauseCondition { get; set; }
        [BsonIgnore]
        public ResumeConfig? ResumeConfig { get; set; }
    }
}
