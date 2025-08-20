using Infrastructure.Services.FlowEngine.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Infrastructure.Services.FlowEngine.Execution
{
    public sealed class ThreadSafeStepExecutionTracker : IStepExecutionTracker
    {
        private readonly ConcurrentDictionary<string, ConcurrentBag<StepExecutionRecord>> _executions = new();
        private readonly ILogger<ThreadSafeStepExecutionTracker> _logger;

        public ThreadSafeStepExecutionTracker(ILogger<ThreadSafeStepExecutionTracker> logger)
        {
            _logger = logger;
        }

        public Task<StepExecutionRecord?> GetLastExecutionAsync(string flowId, string stepId, CancellationToken cancellationToken)
        {
            var key = $"{flowId}:{stepId}";
            if (_executions.TryGetValue(key, out var executions))
            {
                var latest = executions.OrderByDescending(e => e.StartedAt).FirstOrDefault();
                return Task.FromResult(latest);
            }
            return Task.FromResult<StepExecutionRecord?>(null);
        }

        public Task<StepExecutionRecord> RecordStartAsync(string flowId, string stepId, string inputDataHash, CancellationToken cancellationToken)
        {
            var record = new StepExecutionRecord
            {
                FlowId = flowId,
                StepId = stepId,
                InputDataHash = inputDataHash,
                StartedAt = DateTime.UtcNow,
                Status = StepExecutionStatus.Started,
                AttemptNumber = 1
            };

            var key = $"{flowId}:{stepId}";
            var executions = _executions.GetOrAdd(key, _ => new ConcurrentBag<StepExecutionRecord>());
            executions.Add(record);

            return Task.FromResult(record);
        }

        public Task RecordCompletionAsync(StepExecutionRecord record, string outputDataHash, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(StepExecutionRecord record, string errorMessage, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> HasExecutedSuccessfullyAsync(string flowId, string stepId, string inputDataHash, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<StepExecutionRecord>> GetExecutionHistoryAsync(string flowId, string stepId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<StepExecutionRecord>>(Array.Empty<StepExecutionRecord>());
        }

        public Task<bool> IsStepCurrentlyExecutingAsync(string flowId, string stepId, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task MarkStepAsSkippedAsync(string flowId, string stepId, string reason, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
