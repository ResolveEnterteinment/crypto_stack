using Infrastructure.Services.FlowEngine.Exceptions;
using Infrastructure.Services.FlowEngine.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Infrastructure.Services.FlowEngine.Persistence
{
    /// <summary>
    /// In-memory persistence for development/testing
    /// </summary>
    public sealed class InMemoryFlowPersistence : IFlowPersistence
    {
        private readonly ConcurrentDictionary<string, FlowDefinition> _flows = new();
        private readonly ConcurrentDictionary<string, List<FlowEvent>> _events = new();
        private readonly ILogger<InMemoryFlowPersistence> _logger;

        public InMemoryFlowPersistence(ILogger<InMemoryFlowPersistence> logger)
        {
            _logger = logger;
        }

        public Task<FlowStatus> GetFlowStatusAsync(string flowId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_flows.TryGetValue(flowId, out var flow) ? flow.Status : throw new FlowNotFoundException($"Flow {flowId} not found"));
        }

        public Task<bool> CancelFlowAsync(string flowId, string reason, CancellationToken cancellationToken)
        {
            if (_flows.TryGetValue(flowId, out var flow))
            {
                flow.Status = FlowStatus.Cancelled;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<FlowTimeline> GetFlowTimelineAsync(string flowId, CancellationToken cancellationToken)
        {
            var events = _events.GetValueOrDefault(flowId, new List<FlowEvent>());
            var flow = _flows.GetValueOrDefault(flowId);

            return Task.FromResult(new FlowTimeline
            {
                FlowId = flowId,
                Events = events.AsReadOnly(),
                CreatedAt = flow?.CreatedAt ?? DateTime.MinValue,
                CompletedAt = flow?.CompletedAt
            });
        }

        public Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            var flows = _flows.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(query.UserId))
                flows = flows.Where(f => f.UserId == query.UserId);

            if (query.Status.HasValue)
                flows = flows.Where(f => f.Status == query.Status.Value);

            if (!string.IsNullOrEmpty(query.FlowType))
                flows = flows.Where(f => f.GetType().Name == query.FlowType);

            if (query.CreatedAfter.HasValue)
                flows = flows.Where(f => f.CreatedAt >= query.CreatedAfter.Value);

            if (query.CreatedBefore.HasValue)
                flows = flows.Where(f => f.CreatedAt <= query.CreatedBefore.Value);

            var totalCount = flows.Count();
            var items = flows
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(f => new FlowSummary
                {
                    FlowId = f.FlowId,
                    FlowType = f.GetType().Name,
                    Status = f.Status,
                    CurrentStep = f.CurrentStepName,
                    CreatedAt = f.CreatedAt,
                    UpdatedAt = f.UpdatedAt ?? f.CreatedAt,
                    UserId = f.UserId,
                    PauseReason = f.PauseReason,
                    PauseMessage = f.PauseMessage
                })
                .ToArray();

            return Task.FromResult(new PagedResult<FlowSummary>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize
            });
        }

        public Task<T?> LoadFlowAsync<T>(string flowId, CancellationToken cancellationToken) where T : FlowDefinition
        {
            return Task.FromResult(_flows.GetValueOrDefault(flowId) as T);
        }

        public Task SaveFlowAsync(FlowDefinition flow, CancellationToken cancellationToken)
        {
            flow.UpdatedAt = DateTime.UtcNow;
            _flows[flow.FlowId] = flow;
            return Task.CompletedTask;
        }

        public Task<bool> ResumeFlowAsync(string flowId, ResumeReason reason, string resumedBy, string message, CancellationToken cancellationToken)
        {
            if (_flows.TryGetValue(flowId, out var flow) && flow.Status == FlowStatus.Paused)
            {
                flow.Status = FlowStatus.Running;
                flow.UpdatedAt = DateTime.UtcNow;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<FlowDefinition>> GetPausedFlowsForAutoResumeAsync(CancellationToken cancellationToken)
        {
            var pausedFlows = _flows.Values.Where(f => f.Status == FlowStatus.Paused).ToArray();
            return Task.FromResult<IReadOnlyList<FlowDefinition>>(pausedFlows);
        }

        public Task SaveEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            _events.AddOrUpdate(flowEvent.FlowId,
                new List<FlowEvent> { flowEvent },
                (key, existing) =>
                {
                    existing.Add(flowEvent);
                    return existing;
                });
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FlowEvent>> GetEventsAsync(string flowId, CancellationToken cancellationToken)
        {
            var events = _events.GetValueOrDefault(flowId, new List<FlowEvent>());
            return Task.FromResult<IReadOnlyList<FlowEvent>>(events.AsReadOnly());
        }
    }
}
