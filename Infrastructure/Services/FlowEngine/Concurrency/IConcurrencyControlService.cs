namespace Infrastructure.Services.FlowEngine.Concurrency
{
    public interface IConcurrencyControlService
    {
        Task<bool> TryAcquireExecutionSlotAsync(string flowId, CancellationToken cancellationToken);
        Task ReleaseExecutionSlotAsync(string flowId, CancellationToken cancellationToken);
        Task<ConcurrencyStatus> GetStatusAsync(CancellationToken cancellationToken);
    }
}
