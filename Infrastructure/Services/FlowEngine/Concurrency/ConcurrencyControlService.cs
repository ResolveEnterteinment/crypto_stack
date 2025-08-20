using Infrastructure.Services.FlowEngine.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Infrastructure.Services.FlowEngine.Concurrency
{
    public sealed class ConcurrencyControlService : IConcurrencyControlService
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentDictionary<string, DateTime> _activeFlows = new();
        private readonly int _maxConcurrentFlows;

        public ConcurrencyControlService(IOptions<FlowEngineOptions> options)
        {
            _maxConcurrentFlows = options.Value.Performance.MaxConcurrentFlows;
            _semaphore = new SemaphoreSlim(_maxConcurrentFlows, _maxConcurrentFlows);
        }

        public async Task<bool> TryAcquireExecutionSlotAsync(string flowId, CancellationToken cancellationToken)
        {
            var acquired = await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (acquired)
            {
                _activeFlows[flowId] = DateTime.UtcNow;
            }
            return acquired;
        }

        public async Task ReleaseExecutionSlotAsync(string flowId, CancellationToken cancellationToken)
        {
            _activeFlows.TryRemove(flowId, out _);
            _semaphore.Release();
        }

        public Task<ConcurrencyStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ConcurrencyStatus
            {
                MaxConcurrentFlows = _maxConcurrentFlows,
                ActiveFlowCount = _activeFlows.Count,
                ActiveFlows = new Dictionary<string, DateTime>(_activeFlows)
            });
        }
    }
}
