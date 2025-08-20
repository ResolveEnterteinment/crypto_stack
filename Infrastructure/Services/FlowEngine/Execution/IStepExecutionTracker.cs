namespace Infrastructure.Services.FlowEngine.Execution
{
    public interface IStepExecutionTracker
    {
        Task<StepExecutionRecord?> GetLastExecutionAsync(string flowId, string stepId, CancellationToken cancellationToken);
        Task<StepExecutionRecord> RecordStartAsync(string flowId, string stepId, string inputDataHash, CancellationToken cancellationToken);
        Task RecordCompletionAsync(StepExecutionRecord record, string outputDataHash, CancellationToken cancellationToken);
        Task RecordFailureAsync(StepExecutionRecord record, string errorMessage, CancellationToken cancellationToken);
        Task<bool> HasExecutedSuccessfullyAsync(string flowId, string stepId, string inputDataHash, CancellationToken cancellationToken);
        Task<IReadOnlyList<StepExecutionRecord>> GetExecutionHistoryAsync(string flowId, string stepId, CancellationToken cancellationToken);
        Task<bool> IsStepCurrentlyExecutingAsync(string flowId, string stepId, CancellationToken cancellationToken);
        Task MarkStepAsSkippedAsync(string flowId, string stepId, string reason, CancellationToken cancellationToken);
    }
}
