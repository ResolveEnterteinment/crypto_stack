using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Engine
{
    /// <summary>
    /// Implementation of injectable Flow Engine Service
    /// </summary>
    public class FlowEngineService : IFlowEngineService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IFlowExecutor _executor;
        private readonly IFlowPersistence _persistence;
        private readonly IFlowRecovery _recovery;
        private readonly ILogger<FlowEngineService> _logger;
        private readonly IFlowRuntimeStore _runtimeStore;

        public FlowEngineService(
            IFlowExecutor executor,
            IFlowPersistence persistence,
            IFlowRecovery recovery,
            ILogger<FlowEngineService> logger,
            IServiceProvider serviceProvider,
            IFlowRuntimeStore runtimeStore)
        {
            _executor = executor;
            _persistence = persistence;
            _recovery = recovery;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _runtimeStore = runtimeStore;
        }

        public async Task RestoreFlowRuntime()
        {
            try
            {
                _logger.LogInformation("Starting flow runtime objects restoration...");

                // Wait a bit to ensure all services are initialized
                // await Task.Delay(TimeSpan.FromSeconds(5));

                // Step 1: Recover crashed flows (flows that were running when server stopped)
                var flowsToRestore = await _persistence.GetFlowsByStatusesAsync([
                    FlowStatus.Initializing,
                    FlowStatus.Ready,
                    FlowStatus.Running,
                    FlowStatus.Paused]);
                _logger.LogInformation("Restoring runtime flows...");

                var restoreResult = await RestoreRuntimeFlows(flowsToRestore);

                foreach (var flow in _runtimeStore.Flows.Values.Where(f => f.State.Status != FlowStatus.Paused))
                {
                    await _executor.ExecuteAsync(flow, CancellationToken.None);
                }

                _logger.LogInformation($"{restoreResult.FlowsRestored} flows out of {restoreResult.TotalFlowsChecked} restored successfully in {restoreResult.Duration.TotalSeconds} seconds. ");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during flow runtime objects restoration");
            }
        }

        private async Task<RestoreRuntimeResult> RestoreRuntimeFlows(List<FlowState> flowsStates)
        {
            var result = new RestoreRuntimeResult();
            foreach (var flowState in flowsStates)
            {
                result.TotalFlowsChecked++;
                try
                {
                    var flow = await Flow.FromStateAsync(flowState, _serviceProvider);

                    if (flow == null)
                    {
                        result.FlowsFailed++;
                        result.FailedFlowIds.Add(flowState.FlowId.ToString());
                        _logger.LogWarning("Failed to restore flow {FlowId}", flowState.FlowId);
                        continue;
                    }

                    _runtimeStore.Flows.Add(flowState.FlowId, flow);
                    _logger.LogInformation("Restored flow {FlowType} with ID {FlowId}", flow.State.FlowType, flow.Id);

                    result.FlowsRestored++;
                    result.RestoredFlowIds.Add(flowState.FlowId.ToString());
                }
                catch (Exception ex)
                {
                    result.FlowsFailed++;
                    result.FailedFlowIds.Add(flowState.FlowId.ToString());
                    _logger.LogWarning(ex, "Failed to restore flow {FlowId}", flowState.FlowId);
                }
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartedAt;

            return result;
        }

        public async Task<Flow?> GetFlowById(Guid flowId)
        {
            if (_runtimeStore.Flows.TryGetValue(flowId, out var flow))
            {
                // Refresh the service context to ensure it uses current services
                flow.RefreshServiceContext();
                return flow;
            }

            // Resolve from persistence using a fresh scope
            using var scope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IFlowPersistence>();

            var flowDoc = await persistence.GetByFlowId(flowId);
            if (flowDoc != null)
            {
                flow = await Flow.FromStateAsync(flowDoc, _serviceProvider);
                if (flow != null)
                {
                    // Cache the restored flow
                    _runtimeStore.Flows[flowId] = flow;
                    return flow;
                }
            }

            _logger.LogError("Flow {FlowId} not found in runtime store or persistence", flowId);
            return null;
        }

        public async Task<FlowExecutionResult> StartAsync<TFlow>(
            Dictionary<string, object>? initialData = null,
            string? userId = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition
        {
            /*
            var flow = Flow.Create<TFlow>(_serviceProvider, initialData);
            flow.State.UserId = userId ?? "system";
            flow.State.CorrelationId = correlationId ?? Guid.NewGuid().ToString();
            */

            var flow = Flow.Builder(_serviceProvider)
                .ForUser(userId)
                .WithCorrelation(correlationId)
                .WithData(initialData)
                .Build<TFlow>();

            _runtimeStore.Flows.Add(flow.Id, flow);

            _logger.LogInformation("Starting flow {FlowType} with ID {FlowId}",
                typeof(TFlow).Name, flow.Id);

            try
            {
                var result = await _executor.ExecuteAsync(flow, cancellationToken);

                _logger.LogInformation("Flow {FlowId} completed with status {Status}",
                    flow.Id, result.Status);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flow {FlowId} failed with error", flow.Id);
                throw;
            }
        }

        public async Task<FlowExecutionResult> ResumeRuntimeAsync(
            Guid flowId,
            CancellationToken cancellationToken = default)
        {
            if (!_runtimeStore.Flows.TryGetValue(flowId, out var flow))
            {
                throw new FlowNotFoundException($"Flow {flowId} not found in runtime store");
            }

            _logger.LogInformation("Resuming flow {FlowId} from step {CurrentStep}",
                flowId, flow.State.CurrentStepName);

            return await _executor.ExecuteAsync(flow, cancellationToken);
        }

        public Task FireAsync<TFlow>(
            Dictionary<string, object>? initialData = null,
            string? userId = null)
            where TFlow : FlowDefinition
        {
            return Task.Run(async () =>
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

        public FlowStatus? GetStatus(Guid flowId)
        {
            return _runtimeStore.Flows.Values.FirstOrDefault(f => f.Id == flowId)?.Status;
        }

        public async Task<bool> CancelAsync(Guid flowId, string reason = null)
        {
            throw new NotImplementedException();
        }

        public async Task<FlowTimeline> GetTimelineAsync(Guid flowId)
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
        public async Task<bool> ResumeManuallyAsync(Guid flowId, string userId, string reason = null)
        {
            _logger.LogInformation("Manual resume requested for flow {FlowId} by user {UserId}", flowId, userId);
            var resumeResult = await ResumeRuntimeAsync(flowId);
            return resumeResult.Status == FlowStatus.Running;
        }

        public async Task<bool> ResumeByEventAsync(Guid flowId, string eventType, object eventData = null)
        {
            _logger.LogInformation("Event-based resume for flow {FlowId} triggered by {EventType}", flowId, eventType);
            throw new NotImplementedException("Event-based resume not implemented yet");
        }

        public List<Flow> GetPausedFlowsAsync()
        {
            var pausedFlows = _runtimeStore.Flows.Values.Where(f => f.Status == FlowStatus.Paused).ToList();
            return pausedFlows;
        }


        public async Task PublishEventAsync(string eventType, object eventData, string correlationId = null)
        {
            var eventService = _serviceProvider.GetRequiredService<IFlowEventService>();
            await eventService.PublishAsync(eventType, eventData, correlationId);
        }

        // NEW: Health and Statistics methods for health checks
        public async Task<FlowEngineHealth> GetHealth()
        {
            try
            {
                var currentTime = DateTime.UtcNow;

                // Get runtime flow counts
                var runtimeFlows = _runtimeStore.Flows.Values;
                var runningCount = runtimeFlows.Count(f => f.Status == FlowStatus.Running);
                var pausedCount = runtimeFlows.Count(f => f.Status == FlowStatus.Paused);

                // Get recent failures (last 15 minutes)
                var recentFailureQuery = new FlowQuery
                {
                    Status = FlowStatus.Failed,
                    CreatedAfter = currentTime.AddMinutes(-15),
                    PageSize = 1000
                };

                var recentFailures = await _persistence.QueryFlowsAsync(recentFailureQuery);
                var recentFailuresCount = recentFailures.TotalCount;

                // Determine health status
                var isHealthy = recentFailuresCount < 10 && pausedCount < 100; // Thresholds for health

                var status = isHealthy ? "Healthy" :
                            (recentFailuresCount >= 10 ? "Degraded - High Failure Rate" : "Degraded - Too Many Paused Flows");

                return new FlowEngineHealth
                {
                    RunningFlowsCount = runningCount,
                    PausedFlowsCount = pausedCount,
                    RecentFailuresCount = recentFailuresCount,
                    IsHealthy = isHealthy,
                    CheckedAt = currentTime,
                    Status = status,
                    AdditionalInfo = new Dictionary<string, object>
                    {
                        ["total_runtime_flows"] = runtimeFlows.Count(),
                        ["failed_flows_threshold"] = 10,
                        ["paused_flows_threshold"] = 100
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting FlowEngine health information");

                return new FlowEngineHealth
                {
                    RunningFlowsCount = 0,
                    PausedFlowsCount = 0,
                    RecentFailuresCount = 0,
                    IsHealthy = false,
                    CheckedAt = DateTime.UtcNow,
                    Status = "Unhealthy - Error retrieving health data",
                    AdditionalInfo = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message
                    }
                };
            }
        }

        public async Task<FlowEngineStatistics> GetStatistics(TimeSpan timeWindow)
        {
            try
            {
                var startTime = DateTime.UtcNow.Subtract(timeWindow);

                var query = new FlowQuery
                {
                    CreatedAfter = startTime,
                    PageSize = 10000 // Large page size to get comprehensive statistics
                };

                var flows = await _persistence.QueryFlowsAsync(query);

                var totalFlows = flows.TotalCount;
                var completedFlows = flows.Items.Count(f => f.Status == FlowStatus.Completed);
                var failedFlows = flows.Items.Count(f => f.Status == FlowStatus.Failed);
                var runningFlows = flows.Items.Count(f => f.Status == FlowStatus.Running);
                var pausedFlows = flows.Items.Count(f => f.Status == FlowStatus.Paused);
                var cancelledFlows = flows.Items.Count(f => f.Status == FlowStatus.Cancelled);

                var successRate = totalFlows > 0 ? (double)completedFlows / totalFlows * 100 : 100.0;

                // Calculate average execution time for completed flows
                var averageExecutionTime = flows.Items
                    .Where(f => f.Duration.HasValue)
                    .Select(f => f.Duration.Value.TotalSeconds)
                    .DefaultIfEmpty(0)
                    .Average();

                // Group flows by type
                var flowsByType = flows.Items
                    .GroupBy(f => f.FlowType)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Group failures by error message (simplified failure reason)
                var failuresByReason = flows.Items
                    .Where(f => f.Status == FlowStatus.Failed && !string.IsNullOrEmpty(f.ErrorMessage))
                    .GroupBy(f => f.ErrorMessage?.Split('\n')[0] ?? "Unknown Error") // Take first line of error
                    .ToDictionary(g => g.Key, g => g.Count());

                return new FlowEngineStatistics
                {
                    TotalFlows = totalFlows,
                    CompletedFlows = completedFlows,
                    FailedFlows = failedFlows,
                    RunningFlows = runningFlows,
                    PausedFlows = pausedFlows,
                    CancelledFlows = cancelledFlows,
                    SuccessRate = successRate,
                    Period = timeWindow,
                    FlowsByType = flowsByType,
                    FailuresByReason = failuresByReason,
                    AverageExecutionTime = averageExecutionTime
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting FlowEngine statistics for time window {TimeWindow}", timeWindow);

                return new FlowEngineStatistics
                {
                    TotalFlows = 0,
                    CompletedFlows = 0,
                    FailedFlows = 0,
                    RunningFlows = 0,
                    PausedFlows = 0,
                    CancelledFlows = 0,
                    SuccessRate = 0,
                    Period = timeWindow,
                    FlowsByType = new Dictionary<string, int>(),
                    FailuresByReason = new Dictionary<string, int> { ["Error retrieving statistics"] = 1 },
                    AverageExecutionTime = 0
                };
            }
        }
    }
}