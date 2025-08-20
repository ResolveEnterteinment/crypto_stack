using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Engine
{
    /// <summary>
    /// Implementation of injectable Flow Engine Service
    /// </summary>
    public class FlowEngineService : IFlowEngineService
    {
        private readonly IFlowExecutor _executor;
        private readonly IFlowPersistence _persistence;
        private readonly IFlowRecovery _recovery;
        private readonly ILogger<FlowEngineService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public FlowEngineService(
            IFlowExecutor executor,
            IFlowPersistence persistence,
            IFlowRecovery recovery,
            ILogger<FlowEngineService> logger,
            IServiceProvider serviceProvider)
        {
            _executor = executor;
            _persistence = persistence;
            _recovery = recovery;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task<FlowResult<TFlow>> StartAsync<TFlow>(
            object initialData = null,
            string userId = null,
            string correlationId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
        {
            var flowId = Guid.NewGuid().ToString();
            var flow = new TFlow();

            // Initialize flow
            flow.FlowId = flowId;
            flow.UserId = userId ?? "system";
            flow.CorrelationId = correlationId ?? Guid.NewGuid().ToString();
            flow.CreatedAt = DateTime.UtcNow;
            flow.Status = FlowStatus.Initializing;

            // Set initial data
            if (initialData != null)
            {
                flow.SetData(initialData);
            }

            _logger.LogInformation("Starting flow {FlowType} with ID {FlowId}",
                typeof(TFlow).Name, flowId);

            try
            {
                var result = await _executor.ExecuteAsync(flow, cancellationToken);

                _logger.LogInformation("Flow {FlowId} completed with status {Status}",
                    flowId, result.Status);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flow {FlowId} failed with error", flowId);
                throw;
            }
        }

        public async Task<FlowResult<TFlow>> ResumeAsync<TFlow>(
            string flowId,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
        {
            var flow = await _persistence.LoadFlowAsync<TFlow>(flowId);
            if (flow == null)
                throw new FlowNotFoundException($"Flow {flowId} not found");

            _logger.LogInformation("Resuming flow {FlowId} from step {CurrentStep}",
                flowId, flow.CurrentStepName);

            return await _executor.ExecuteAsync(flow, cancellationToken);
        }

        public async Task FireAsync<TFlow>(
            object initialData = null,
            string userId = null)
            where TFlow : FlowDefinition, new()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await StartAsync<TFlow>(initialData, userId);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Fire-and-forget flow {FlowType} failed", typeof(TFlow).Name);
                }
            });
        }

        public async Task<FlowResult<TTriggered>> TriggerAsync<TTriggered>(
            FlowContext context,
            object triggerData = null)
            where TTriggered : FlowDefinition, new()
        {
            var correlationId = $"{context.Flow.CorrelationId}:triggered:{typeof(TTriggered).Name}";

            return await StartAsync<TTriggered>(
                triggerData,
                context.Flow.UserId,
                correlationId,
                context.CancellationToken);
        }

        public async Task<FlowStatus> GetStatusAsync(string flowId)
        {
            return await _persistence.GetFlowStatusAsync(flowId);
        }

        public async Task<bool> CancelAsync(string flowId, string reason = null)
        {
            return await _persistence.CancelFlowAsync(flowId, reason);
        }

        public async Task<FlowTimeline> GetTimelineAsync(string flowId)
        {
            return await _persistence.GetFlowTimelineAsync(flowId);
        }

        public async Task<PagedResult<FlowSummary>> QueryAsync(FlowQuery query)
        {
            return await _persistence.QueryFlowsAsync(query);
        }

        public async Task<RecoveryResult> RecoverCrashedFlowsAsync()
        {
            return await _recovery.RecoverAllAsync();
        }

        public async Task<int> CleanupAsync(TimeSpan olderThan)
        {
            return await _persistence.CleanupCompletedFlowsAsync(olderThan);
        }

        // NEW: Pause/Resume implementation
        public async Task<bool> ResumeManuallyAsync(string flowId, string userId, string reason = null)
        {
            _logger.LogInformation("Manual resume requested for flow {FlowId} by user {UserId}", flowId, userId);
            return await _persistence.ResumeFlowAsync(flowId, ResumeReason.Manual, userId, reason);
        }

        public async Task<bool> ResumeByEventAsync(string flowId, string eventType, object eventData = null)
        {
            _logger.LogInformation("Event-based resume for flow {FlowId} triggered by {EventType}", flowId, eventType);
            return await _persistence.ResumeFlowAsync(flowId, ResumeReason.Event, $"system", $"Event: {eventType}");
        }

        public async Task<PagedResult<FlowSummary>> GetPausedFlowsAsync(FlowQuery query = null)
        {
            query ??= new FlowQuery();
            query.Status = FlowStatus.Paused;
            return await _persistence.QueryFlowsAsync(query);
        }

        public async Task<bool> SetResumeConditionAsync(string flowId, ResumeCondition condition)
        {
            return await _persistence.SetResumeConditionAsync(flowId, condition);
        }

        public async Task PublishEventAsync(string eventType, object eventData, string correlationId = null)
        {
            var eventService = _serviceProvider.GetRequiredService<IFlowEventService>();
            await eventService.PublishAsync(eventType, eventData, correlationId);
        }

        public async Task<int> CheckAutoResumeConditionsAsync()
        {
            var autoResumeService = _serviceProvider.GetRequiredService<IFlowAutoResumeService>();
            return await autoResumeService.CheckAndResumeFlowsAsync();
        }
    }
}
