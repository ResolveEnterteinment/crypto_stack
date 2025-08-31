using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Services.FlowEngine.Services.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Infrastructure.Services.FlowEngine.Engine;

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

                foreach (var flow in _runtimeStore.Flows.Values.Where(f => f.Status != FlowStatus.Paused))
                {
                    await _executor.ExecuteAsync(flow, CancellationToken.None);
                }

                // Step 2: Restore paused flows and their resume conditions
                //_logger.LogInformation("Restoring paused flows runtime state...");
                //await RestorePausedFlowsAsync();

                // Step 3: Start auto-resume background service
                //_logger.LogInformation("Starting auto-resume background service...");
                //var autoResumeService = _serviceProvider.GetRequiredService<IFlowAutoResumeService>();
                //await autoResumeService.StartBackgroundCheckingAsync();

                _logger.LogInformation($"{restoreResult.FlowsRestored} flows out of {restoreResult.TotalFlowsChecked} restored successfully in {restoreResult.Duration.TotalSeconds} seconds. ");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during flow runtime objects restoration");
            }
        }

        private Task<RestoreRuntimeResult> RestoreRuntimeFlows(List<FlowDocument> flowsToRestore)
        {
            var result = new RestoreRuntimeResult();
            foreach (var flowDoc in flowsToRestore)
            {
                result.TotalFlowsChecked++;
                try
                {
                    // Create an instance of Type flowDoc.FlowType using DI
                    var flowType = Type.GetType(flowDoc.FlowType);
                    if (flowType == null)
                    {
                        result.FlowsFailed++;
                        result.FailedFlowIds.Add(flowDoc.FlowId.ToString());
                        _logger.LogWarning("Could not resolve flow type {FlowType} for flow {FlowId}", flowDoc.FlowType, flowDoc.FlowId);
                        continue;
                    }

                    // Try to get from DI container first (recommended)
                    FlowDefinition flow = null;
                    try
                    {
                        flow = (FlowDefinition)_serviceProvider.GetRequiredService(flowType);

                    }
                    catch (InvalidOperationException)
                    {
                        // Fallback to Activator if not registered in DI
                        _logger.LogWarning("Flow type {FlowType} not registered in DI container, using Activator fallback", flowType.Name);
                        flow = (FlowDefinition)Activator.CreateInstance(flowType);
                    }

                    if (flow == null)
                    {
                        result.FlowsFailed++;
                        result.FailedFlowIds.Add(flowDoc.FlowId.ToString());
                        _logger.LogWarning("Failed to initiate flow {FlowType} using Activator fallback", flowDoc.FlowType);
                        continue;
                    }

                    // Initialize the flow to ensure steps are properly defined
                    flow.Initialize();

                    // Instead of deserializing directly, copy the state from the document
                    // Populate the flow with persisted data

                    CopyFlowState(flowDoc, flow);

                    if(flow.Status == FlowStatus.Paused)
                    {
                        var pauseStep = flow.Steps[flow.CurrentStepIndex];

                        var context = new FlowContext
                        {
                            Flow = flow,
                            CurrentStep = pauseStep,
                            CancellationToken = default,
                            Services = _serviceProvider
                        };

                        var pauseCondition = pauseStep.PauseCondition(context);

                        flow.ActiveResumeConfig = pauseCondition.ResumeConfig ?? pauseStep.ResumeConfig;

                        _logger.LogDebug("Restored pause condition for flow {FlowId} at step {StepName}", flow.FlowId, pauseStep.Name);
                    }
                    _runtimeStore.Flows.Add(flowDoc.FlowId, flow);
                    _logger.LogInformation("Restored flow {FlowType} with ID {FlowId}", flow.GetType().Name, flow.FlowId);

                    result.FlowsRestored++;
                    result.RestoredFlowIds.Add(flowDoc.FlowId.ToString());
                }
                catch (Exception ex)
                {
                    result.FlowsFailed++;
                    result.FailedFlowIds.Add(flowDoc.FlowId.ToString());
                    _logger.LogWarning(ex, "Failed to restore flow {FlowId}", flowDoc.FlowId);
                }
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt - result.StartedAt;

            return Task.FromResult(result);
        }

        private FlowDefinition? RestoreRuntimeFlow(FlowDocument flowDoc)
        {
            try
            {
                // Create an instance of Type flowDoc.FlowType using DI
                var flowType = Type.GetType(flowDoc.FlowType);
                if (flowType == null)
                {
                    _logger.LogWarning("Could not resolve flow type {FlowType} for flow {FlowId}", flowDoc.FlowType, flowDoc.FlowId);
                    throw new FlowExecutionException($"Could not resolve flow type {flowDoc.FlowType} for flow {flowDoc.FlowId}");
                }

                // Try to get from DI container first (recommended)
                FlowDefinition flow = null;
                try
                {
                    flow = (FlowDefinition)_serviceProvider.GetRequiredService(flowType);
                }
                catch (InvalidOperationException)
                {
                    // Fallback to Activator if not registered in DI
                    _logger.LogWarning("Flow type {FlowType} not registered in DI container, using Activator fallback", flowType.Name);
                    flow = (FlowDefinition)Activator.CreateInstance(flowType);
                }

                if (flow == null)
                {
                    _logger.LogWarning("Failed to initiate flow {FlowType} using Activator fallback", flowDoc.FlowType);
                    throw new FlowExecutionException($"Failed to initiate flow {flowDoc.FlowType} using Activator fallback");
                }

                // Initialize the flow to ensure steps are properly defined
                flow.Initialize();

                // Instead of deserializing directly, copy the state from the document
                // Populate the flow with persisted data

                CopyFlowState(flowDoc, flow);

                if (flow.Status == FlowStatus.Paused)
                {
                    var pauseStep = flow.Steps[flow.CurrentStepIndex];

                    var context = new FlowContext
                    {
                        Flow = flow,
                        CurrentStep = pauseStep,
                        CancellationToken = default,
                        Services = _serviceProvider
                    };

                    var pauseCondition = pauseStep.PauseCondition(context);

                    flow.ActiveResumeConfig = pauseCondition.ResumeConfig ?? pauseStep.ResumeConfig;

                    _logger.LogDebug("Restored pause condition for flow {FlowId} at step {StepName}", flow.FlowId, pauseStep.Name);
                }
                _runtimeStore.Flows.Add(flowDoc.FlowId, flow);
                _logger.LogInformation("Restored flow {FlowType} with ID {FlowId}", flow.GetType().Name, flow.FlowId);
                
                return flow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore flow {FlowId}", flowDoc.FlowId);
                return null;
            }
        }
        public async Task<FlowDefinition?> GetFlowById(Guid flowId)
        {
            if (_runtimeStore.Flows.TryGetValue(flowId, out var flow))
            {
                return flow;
            }
            else
            {
                //resolve from persistence
                var flowDoc = await _persistence.GetByFlowId(flowId);
                flow = RestoreRuntimeFlow(flowDoc);
                if (flow != null)
                {
                    return flow;
                }
            }

            _logger.LogError("Flow {FlowId} not found in runtime store", flowId);
            return null;
        }

        public async Task<FlowResult<TFlow>> StartAsync<TFlow>(
            Dictionary<string, object>? initialData = null,
            string userId = null,
            string correlationId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition
        {
            var flowId = Guid.NewGuid();
            
            // ✅ Get flow from DI container instead of new()
            var flow = _serviceProvider.GetRequiredService<TFlow>();

            // Initialize flow
            flow.FlowId = flowId;
            flow.UserId = userId ?? "system";
            flow.CorrelationId = correlationId ?? Guid.NewGuid().ToString();
            flow.CreatedAt = DateTime.UtcNow;
            flow.Status = FlowStatus.Initializing;

            // Set initial data
            if (initialData != null)
            {
                flow.SetInitialData(initialData);
            }

            _runtimeStore.Flows.Add(flowId, flow);
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

        public async Task<FlowResult<FlowDefinition>> ResumeRuntimeAsync(
            Guid flowId,
            CancellationToken cancellationToken = default)
        {
            if(!_runtimeStore.Flows.TryGetValue(flowId, out var flow))
            {
                throw new FlowNotFoundException($"Flow {flowId} not found in runtime store");
            }

            _logger.LogInformation("Resuming flow {FlowId} from step {CurrentStep}",
                flowId, flow.CurrentStepName);

            return await _executor.ExecuteAsync(flow, cancellationToken);
        }

        /// <summary>
        /// Copy flow state from persisted instance to fresh DI instance
        /// </summary>
        private void CopyFlowState<TFlow>(FlowDocument source, TFlow target) where TFlow : FlowDefinition
        {
            target.FlowId = source.FlowId;
            target.UserId = source.UserId;
            target.CorrelationId = source.CorrelationId;
            target.CreatedAt = source.CreatedAt;
            target.StartedAt = source.StartedAt;
            target.CompletedAt = source.CompletedAt;
            target.Status = source.Status;
            target.CurrentStepName = source.CurrentStepName;
            target.CurrentStepIndex = source.CurrentStepIndex;
            target.Data = source.Data ?? [];
            target.Events = source.Events ?? new List<FlowEvent>();
            target.LastError = source.LastError;

            // Copy pause/resume state
            target.PausedAt = source.PausedAt;
            target.PauseReason = source.PauseReason;
            target.PauseMessage = source.PauseMessage;
            target.PauseData = source.PauseData ?? [];

            // Fix for CS0272: Use the collection initializer to populate the Steps property
            if (source.Steps != null)
            {
                foreach (var step in source.Steps)
                {
                    var tagetStep = target.Steps.Find(s => s.Name == step.Name);
                    if (tagetStep == null) throw new FlowExecutionException("Failed to copy step state. Flow step not found.");

                    tagetStep.Status = step.Status;
                    if (step.Result != null) tagetStep.Result = step.Result;

                    if(step.Branches != null && step.Branches.Count > 0)
                    {
                        for (var i = 0; i < step.Branches.Count; i++)
                        {
                            var branch = step.Branches[i];
                            var targetBranch = tagetStep.Branches.ElementAt(i);
                            if (targetBranch == null) throw new FlowExecutionException("Failed to copy branch step state. Branch step not found.");
                            foreach (var branchStep in branch.Steps)
                            {
                                var targetBranchStep = targetBranch.Steps.Find(s => s.Name == branchStep.Name);
                                if (targetBranchStep == null) throw new FlowExecutionException("Failed to copy branch step state. Branch step not found.");

                                targetBranchStep.Status = branchStep.Status;
                                if (branchStep.Result != null) targetBranchStep.Result = branchStep.Result;
                            }
                        }
                    }

                }
            }
        }

        public async Task FireAsync<TFlow>(
            Dictionary<string, object>? initialData = null,
            string userId = null)
            where TFlow : FlowDefinition
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
            Dictionary<string, object>? triggerData = null)
            where TTriggered : FlowDefinition
        {
            var correlationId = $"{context.Flow.CorrelationId}:triggered:{typeof(TTriggered).Name}";

            return await StartAsync<TTriggered>(
                triggerData,
                context.Flow.UserId,
                correlationId,
                context.CancellationToken);
        }

        public FlowStatus? GetStatus(Guid flowId)
        {
            return _runtimeStore.Flows.Values.FirstOrDefault(f => f.FlowId == flowId)?.Status;
        }

        public async Task<bool> CancelAsync(Guid flowId, string reason = null)
        {
            return await _persistence.CancelFlowAsync(flowId, reason);
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
            var resumeResult=  await ResumeRuntimeAsync(flowId);
            return resumeResult.Status == FlowStatus.Running;
        }

        public async Task<bool> ResumeByEventAsync(Guid flowId, string eventType, object eventData = null)
        {
            _logger.LogInformation("Event-based resume for flow {FlowId} triggered by {EventType}", flowId, eventType);
            return await _persistence.ResumeFlowAsync(flowId, ResumeReason.Event, $"system", $"Event: {eventType}");
        }

        public async Task<PagedResult<FlowSummary>> GetPausedFlowSummariesAsync(FlowQuery query = null)
        {
            query ??= new FlowQuery();
            query.Status = FlowStatus.Paused;
            return await _persistence.QueryFlowsAsync(query);
        }

        public List<FlowDefinition> GetPausedFlowsAsync()
        {
            var pausedFlows = _runtimeStore.Flows.Values.Where(f => f.Status == FlowStatus.Paused).ToList();
            return pausedFlows;
        }

        public async Task<bool> SetResumeConditionAsync(Guid flowId, ResumeCondition condition)
        {
            return await _persistence.SetResumeConditionAsync(flowId, condition);
        }

        public async Task PublishEventAsync(string eventType, object eventData, string correlationId = null)
        {
            var eventService = _serviceProvider.GetRequiredService<IFlowEventService>();
            await eventService.PublishAsync(eventType, eventData, correlationId);
        }

    }
}
