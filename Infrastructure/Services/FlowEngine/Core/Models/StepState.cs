using Domain.DTOs.Flow;
using Infrastructure.Services.FlowEngine.Core.Enums;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class StepState
    {
        public string Name { get; set; }
        public StepStatus Status { get; set; }
        public List<string> StepDependencies { get; set; }
        public Dictionary<string, string> DataDependencies { get; set; }
        public List<FlowBranch> Branches { get; set; } = [];
        public StepResult? Result { get; set; }
        public SerializableError? Error { get; set; }
        public int MaxRetries { get; set; }
        public TimeSpan RetryDelay { get; set; }
        public TimeSpan? Timeout { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsCritical { get; set; }
        public bool IsIdempotent { get; set; }
        public bool CanRunInParallel { get; set; }
        public List<TriggeredFlowData> TriggeredFlows { get; set; } = [];
        public string? JumpTo { get; set; }
        public int? MaxJumps { get; set; } = null;
        public int CurrentJumps { get; set; } = 0;

        public StepState(FlowStep step)
        {
            Name = step.Name;
            StepDependencies = step.StepDependencies ?? new List<string>();
            DataDependencies = step.DataDependencies?
                .Where(kvp => kvp.Value != null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FullName!)
                ?? [];
            Branches = step.Branches ?? [];
            Result = step.Result;
            Error = step.Error;
            MaxRetries = step.MaxRetries;
            RetryDelay = step.RetryDelay;
            Timeout = step.Timeout;
            StartedAt = step.StartedAt;
            CompletedAt = step.CompletedAt;
            Duration = step.Duration;
            IsCritical = step.IsCritical;
            IsIdempotent = step.IsIdempotent;
            CanRunInParallel = step.CanRunInParallel;
            Status = step.Status;
            TriggeredFlows = step.TriggeredFlows;
            JumpTo = step.JumpTo;
            MaxJumps = step.MaxJumps;
            CurrentJumps = step.CurrentJumps;
        }
    }
}
