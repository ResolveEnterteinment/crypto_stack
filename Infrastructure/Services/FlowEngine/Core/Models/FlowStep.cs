using Domain.DTOs.Flow;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    [BsonIgnoreExtraElements]
    public class FlowStep
    {
        public string Name { get; set; } = "Unnamed Step";
        public StepStatus Status { get; set; } = StepStatus.Pending;
        [BsonIgnore]
        [JsonIgnore]
        public Func<FlowExecutionContext, Task<StepResult>>? ExecuteAsync { get; set; }
        public List<string> StepDependencies { get; set; } = [];
        public Dictionary<string, Type> DataDependencies { get; internal set; } = [];
        public List<string> RequiredRoles { get; set; } = [];
        [BsonIgnore]
        [JsonIgnore]
        public Func<FlowExecutionContext, bool>? Condition { get; set; }
        public StepResult? Result { get; set; }

        public SerializableError? Error { get; set; }

        public bool CanRunInParallel { get; set; } // Functionality not implemented yet
        public int MaxRetries { get; set; } = 0;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan? Timeout { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration => CompletedAt - StartedAt;
        public List<Type> Middleware { get; set; } = [];
        public List<FlowBranch> Branches { get; set; } = [];
        public DynamicBranchingConfig? DynamicBranching { get; set; }
        public List<TriggeredFlowData> TriggeredFlows { get; set; } = [];
        public string? JumpTo { get; set; } = null;
        public int? MaxJumps { get; set; } = null;
        public int CurrentJumps { get; set; } = 0;
        public bool IsCritical { get; set; } = false;
        public bool AllowFailure { get; set; } = false;
        public bool IsIdempotent { get; set; } = false;
        public Func<FlowExecutionContext, string>? IdempotencyKeyFactory { get; set; } = null;
        public string? IdempotencyKey { get; set; } = null;

        // NEW: Pause/Resume capabilities
        [BsonIgnore]
        [JsonIgnore]
        public Func<FlowExecutionContext, Task<PauseCondition>>? PauseCondition { get; set; }
        [BsonIgnore]
        [JsonIgnore]
        public ResumeConfig? ResumeConfig { get; set; }

        public StepResult Cancel(string? reason = null)
        {
            Status = StepStatus.Cancelled;
            return StepResult.Cancel($"Step {Name} was cancelled. {reason}");
        }

        public StepResult Failure(string message, Exception? ex = null)
        {
            Status = StepStatus.Failed;
            Error =  SerializableError.FromException(ex ?? new Exception(message));
            return StepResult.Failure(message, ex);
        }

        public StepResult Success(string? message = null, Dictionary<string, object>? data = null)
        {
            Status = StepStatus.Completed;
            return StepResult.Success(message, data);
        }
    }
}