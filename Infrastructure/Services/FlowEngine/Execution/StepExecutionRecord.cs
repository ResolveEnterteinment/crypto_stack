using Infrastructure.Services.FlowEngine.Models;

namespace Infrastructure.Services.FlowEngine.Execution
{
    public sealed record StepExecutionRecord
    {
        public string FlowId { get; init; } = string.Empty;
        public string StepId { get; init; } = string.Empty;
        public int AttemptNumber { get; init; }
        public string InputDataHash { get; init; } = string.Empty;
        public string OutputDataHash { get; init; } = string.Empty;
        public DateTime StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public StepExecutionStatus Status { get; init; }
        public string? ErrorMessage { get; init; }
        public TimeSpan? Duration => CompletedAt?.Subtract(StartedAt);
    }
}
