using Application.Interfaces;
using Infrastructure.Hubs;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stripe;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services.FlowEngine.Engine
{
    public class FlowExecutor : IFlowExecutor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FlowExecutor> _logger;
        private readonly IIdempotencyService _idempotency;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IFlowNotificationService _notificationService;

        public FlowExecutor(
            IServiceProvider serviceProvider, 
            ILogger<FlowExecutor> logger, 
            IServiceScopeFactory scopeFactory,
            IHubContext<FlowHub> hubContext)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _idempotency = serviceProvider.GetRequiredService<IIdempotencyService>();
            _notificationService = serviceProvider.GetRequiredService<IFlowNotificationService>();
            _scopeFactory = scopeFactory;
        }

        public async Task<FlowExecutionResult> ExecuteAsync(Flow flow, CancellationToken cancellationToken)
        {            
            try
            {
                flow.State.Status = FlowStatus.Running;
                flow.State.StartedAt = DateTime.UtcNow;

                // Send initial status update
                await _notificationService.NotifyFlowStatusChanged(flow);

                while (flow.State.CurrentStepIndex < flow.Definition.Steps.Count)
                {
                    var step = flow.Definition.Steps[flow.State.CurrentStepIndex];

                    flow.Context.CurrentStep = step;
                    flow.State.CurrentStepName = step.Name;
                    flow.State.CurrentStepIndex = flow.Definition.Steps.IndexOf(step);

                    await ExecuteStepAsync(flow, step, cancellationToken);

                    await flow.PersistAsync();

                    // Send step completion notification
                    await _notificationService.NotifyStepStatusChanged(flow, step);

                    if (flow.State.Status == FlowStatus.Paused || cancellationToken.IsCancellationRequested)
                        break;

                    if (flow.Definition.Steps.Count <= flow.State.CurrentStepIndex + 1)
                        break; // No more steps to execute
                    flow.State.CurrentStepIndex++;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return FlowExecutionResult.Cancelled(flow.State.FlowId, "Flow execution was cancelled");
                }

                if (flow.State.Status == FlowStatus.Paused)
                {
                    return FlowExecutionResult.Paused(flow.State.FlowId, "Flow is paused");
                }

                flow.State.Status = FlowStatus.Completed;
                flow.State.CompletedAt = DateTime.UtcNow;

                await flow.PersistAsync();

                // Send completion notification
                await _notificationService.NotifyFlowStatusChanged(flow);

                return FlowExecutionResult.Success(flow.State.FlowId, "Flow completed successfully");
            }
            catch (Exception ex)
            {
                flow.State.Status = FlowStatus.Failed;
                flow.State.LastError = ex;
                flow.State.CompletedAt = DateTime.UtcNow;

                flow.State.Events.Add(new FlowEvent
                {
                    FlowId = flow.State.FlowId,
                    EventType = "FlowFailed",
                    Description = $"Flow failed with {ex.GetType().Name} error.",
                    Data = new Dictionary<string, object>
                    {
                        { "errorMessage", ex.Message },
                        { "stackTrace", ex.StackTrace }
                    }.ToSafe(),
                });

                _logger.LogError(ex, "Flow {FlowId} failed at step {StepName}: {ErrorMessage}", flow.State.FlowId, flow.State.CurrentStepName, ex.Message);

                await flow.PersistAsync();

                // Send error notification
                await _notificationService.NotifyFlowError(flow.State.FlowId, ex.Message);
                await _notificationService.NotifyFlowStatusChanged(flow);

                return FlowExecutionResult.Failure(flow.State.FlowId, ex.Message, ex);
            }
        }

        private async Task ExecuteStepAsync(Flow flow, FlowStep step, CancellationToken cancellationToken)
        {
            try
            {
                // Check conditional execution
                if (step.Condition != null && step.Condition?.Invoke(flow.Context) != true)
                {
                    step.Status = StepStatus.Skipped;

                    await flow.PersistAsync();

                    await _notificationService.NotifyStepStatusChanged(flow, step);
                    return; // Skip step execution
                }

                // Check data dependencies
                foreach (var dependency in step.DataDependencies)
                {
                    if (!flow.State.Data.TryGetValue(dependency.Key, out SafeObject? value))
                    {
                        step.Status = StepStatus.Failed;
                        throw new FlowExecutionException($"Missing required data for step {step.Name}: {dependency.Key}");
                    }

                    if (value.Type != dependency.Value.FullName)
                    {
                        step.Status = StepStatus.Failed;
                        throw new FlowExecutionException($"Invalid required data type for step {step.Name}: {dependency.Value.Name}");
                    }
                }

                step.Status = StepStatus.InProgress;

                // NEW: Check if step should pause
                if (step.PauseCondition != null)
                {
                    var pauseCondition = step.PauseCondition(flow.Context);
                    if (pauseCondition.ShouldPause)
                    {
                        step.Status = StepStatus.Paused;
                        await PauseInternalAsync(flow, pauseCondition);
                        return; // Exit step execution as flow is paused
                    }
                }


                _logger.LogInformation("Executing step {StepName} in flow {FlowId}", step.Name, flow.State.FlowId);

                // Check if step is idempotent
                string? idempotencyKey = null;

                if (step.IsIdempotent)
                {
                    // Generate idempotency key based on step context and data dependencies
                    idempotencyKey = GenerateStepIdempotencyKey(flow, step, flow.Context);

                    // Check if this step was already executed successfully
                    var (resultExists, existingResult) = await _idempotency.GetResultAsync<StepResult>(idempotencyKey);

                    if (resultExists && existingResult != null)
                    {
                        _logger.LogInformation("Idempotent step {StepName} already executed successfully, returning cached result", step.Name);

                        // Merge cached result data into flow context if available
                        if (existingResult.Data != null && existingResult.Data.Count > 0)
                        {
                            foreach (var kvp in existingResult.Data)
                            {
                                flow.State.Data[kvp.Key] = kvp.Value;
                            }

                            step.Result = StepResult.ConcurrencyConflict(data: existingResult.Data.FromSafe());
                        }

                        // Skip execution and continue to next step
                        return;
                    }
                }

                // Execute main step logic
                if (step.ExecuteAsync != null)
                {
                    try
                    {
                        var result = await step.ExecuteAsync(flow.Context);

                        step.Result = result;

                        if (result == null || !result.IsSuccess)
                        {
                            throw new FlowExecutionException($"Step {step.Name} failed: {result?.Message ?? "Step result returned null,"}");
                        }

                        if (result.Data != null && result.Data.Count != 0)
                        {
                            // Merge result data into flow context
                            foreach (var kvp in result.Data)
                            {
                                flow.State.Data[kvp.Key] = kvp.Value;
                            }

                            if (step.IsIdempotent)
                            {
                                // Store the successful result for future idempotency checks
                                await _idempotency.StoreResultAsync(idempotencyKey!, result, TimeSpan.FromHours(24));
                            }
                        }
                    }
                    catch (Exception)
                    {
                        step.Status = StepStatus.Failed;

                        if (step.AllowFailure)
                        {
                            _logger.LogWarning("Step {StepName} failed but is allowed to fail, continuing flow", step.Name);
                        }
                        else
                        {
                            _logger.LogError("Step {StepName} failed, aborting flow", step.Name);
                            throw; // Rethrow to be caught by outer flow execution handler
                        }
                    }
                }

                // Handle static conditional branching
                if (step.Branches.Count > 0)
                {
                    await ExecuteStaticBranches(flow.Context, step.Branches, cancellationToken);
                }

                // Handle dynamic sub-branching
                if (step.DynamicBranching != null)
                {
                    await ExecuteDynamicSubSteps(flow.Context, step.DynamicBranching, cancellationToken);
                }

                // Trigger other flows if configured
                if (step.TriggeredFlows != null && step.TriggeredFlows.Count > 0)
                {
                    try
                    {
                        var tasks = step.TriggeredFlows.Select(flowType => TriggerFlow(flow.Context, flowType));
                        Task.WhenAll(tasks);
                    }
                    catch (Exception ex)
                    {
                        // Triggering external flows should not fail this flow.
                        _logger.LogWarning(ex, "Failed to trigger flow");
                    }
                }

                // Handle jump to another step if configured
                if (!string.IsNullOrWhiteSpace(step.JumpTo))
                {
                    if (step.MaxJumps.HasValue && step.CurrentJumps >= step.MaxJumps.Value)
                    {
                        step.CurrentJumps = 0; // Reset jump counter
                        throw new FlowExecutionException($"Step {step.Name} exceeded maximum jumps to {step.JumpTo} (max {step.MaxJumps})");
                    }

                    var targetIndex = flow.Definition.Steps.FindIndex(s => s.Name == step.JumpTo);
                    if (targetIndex == -1)
                    {
                        throw new FlowExecutionException($"Step {step.Name} attempted to jump to unknown step {step.JumpTo}");
                    }
                    flow.State.CurrentStepIndex = targetIndex - 1; // -1 because the main loop will increment it
                    step.CurrentJumps++;
                }

                step.Status = StepStatus.Completed;
            }
            catch (Exception)
            {
                step.Status = StepStatus.Failed;
                throw;
            }
            
        }

        /// <summary>
        /// Pause the flow with the specified condition
        /// </summary>
        private async Task<FlowExecutionResult> PauseInternalAsync(Flow flow, PauseCondition pauseCondition)
        {
            flow.State.Status = FlowStatus.Paused;
            flow.State.PausedAt = DateTime.UtcNow;
            flow.State.PauseReason = pauseCondition.Reason;
            flow.State.PauseMessage = pauseCondition.Message;
            flow.State.PauseData = pauseCondition.Data.ToSafe();

            var step = flow.Definition.Steps.ElementAtOrDefault(flow.State.CurrentStepIndex);

            // Set resume configuration from step or pause condition
            flow.Definition.ActiveResumeConfig = pauseCondition.ResumeConfig ?? step.ResumeConfig;

            _logger.LogInformation("Flow {FlowId} paused at step {StepName}: {Reason} - {Message}",
                flow.State.FlowId, step.Name, pauseCondition.Reason, pauseCondition.Message);

            // Add pause event to timeline
            flow.State.Events.Add(new FlowEvent
            {
                FlowId = flow.State.FlowId,
                EventType = "FlowPaused",
                Description = $"Flow paused: {pauseCondition.Message}",
                Data = new Dictionary<string, object>
                {
                    ["Reason"] = pauseCondition.Reason.ToString(),
                    ["StepName"] = step.Name,
                    ["PauseData"] = pauseCondition.Data
                }.ToSafe(),
            });

            // Save paused state to persistence
            await flow.PersistAsync();

            await _notificationService.NotifyFlowStatusChanged(flow);
            await _notificationService.NotifyStepStatusChanged(flow, step);

            return FlowExecutionResult.Paused(flow.State.FlowId, "Flow is paused");
        }

        /// <summary>
        /// Resume flow internally (update local state)
        /// </summary>
        public async Task<FlowExecutionResult> ResumePausedFlowAsync(Flow flow, string reason, CancellationToken cancellationToken)
        {
            flow.State.Status = FlowStatus.Running;
            flow.State.PauseReason = null;
            flow.State.PausedAt = null;
            flow.State.PauseMessage = null;
            flow.State.Events.Add(new FlowEvent
            {
                EventId = Guid.NewGuid(),
                FlowId = flow.State.FlowId,
                EventType = "FlowResumed",
                Description = $"Flow resumed by {"system"} (reason: {reason})",
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object>
                {
                    { "resumeReason", reason.ToString() },
                    { "resumedBy", "system" }
                }.ToSafe()
            });

            var pausedStep = flow.Definition.Steps[flow.State.CurrentStepIndex];
            if (pausedStep != null && pausedStep.Status == StepStatus.Paused)
            {
                pausedStep.PauseCondition = null; // Clear pause condition to avoid re-pausing
            }

            await flow.PersistAsync();

            Task.Run(async() => await ExecuteAsync(flow, cancellationToken)); // Fire and forget

            await _notificationService.NotifyFlowStatusChanged(flow);
            await _notificationService.NotifyStepStatusChanged(flow, pausedStep);

            _logger.LogInformation("Flow {FlowId} resumed internally: {Reason}", flow.State.FlowId, reason);
            return FlowExecutionResult.Resumed(flow.State.FlowId, "Flow is resumed");
        }

        public async Task<FlowExecutionResult> RetryFailedFlowAsync(Flow flow, string reason, CancellationToken cancellationToken)
        {
            if (flow.State.Status != FlowStatus.Failed)
            {
                return FlowExecutionResult.Failure(flow.State.FlowId, "Only failed flows can be retried");
            }

            var failedStepIndex = flow.State.CurrentStepIndex;
            if (failedStepIndex < 0 || failedStepIndex >= flow.Definition.Steps.Count)
            {
                return FlowExecutionResult.Failure(flow.State.FlowId, "Invalid current step index for retry");
            }

            // Reset state to before the failed step
            flow.State.Status = FlowStatus.Running;
            flow.State.LastError = null;
            flow.State.CompletedAt = null;

            var failedStep = flow.Definition.Steps[failedStepIndex];
            if (failedStep != null && failedStep.Status == StepStatus.Failed)
            {
                failedStep.Status = StepStatus.Pending; // Reset step status for retry
                failedStep.Result = null; // Clear previous result
            }

            flow.State.Events.Add(new FlowEvent
            {
                FlowId = flow.State.FlowId,
                EventType = "FlowRetried",
                Description = $"Flow retried by {"system"} (reason: {reason})",
                Data = new Dictionary<string, object>
                {
                    { "retryReason", reason },
                    { "retriedBy", "system" },
                    { "stepName", failedStep.Name }
                }.ToSafe()
            });

            await flow.PersistAsync();

            await _notificationService.NotifyFlowStatusChanged(flow);
            await _notificationService.NotifyStepStatusChanged(flow, failedStep);

            _logger.LogInformation("Retrying failed flow {FlowId} from step {StepName}", flow.State.FlowId, failedStep.Name);
            
            Task.Run(async () => await ExecuteAsync(flow, cancellationToken)); // Fire and forget

            await _notificationService.NotifyFlowStatusChanged(flow);

            return FlowExecutionResult.Success(flow.State.FlowId, "Flow is retried");

        }
        /// <summary>
        /// Execute dynamic sub-steps based on runtime data
        /// </summary>
        private async Task ExecuteDynamicSubSteps(FlowExecutionContext context, DynamicBranchingConfig config, CancellationToken cancellationToken)
        {
            // Get data items that will become sub-steps
            var dataItems = config.DataSelector(context).ToList();

            if (dataItems.Count == 0)
            {
                _logger.LogInformation("No data items found for dynamic branching in step {StepName}", context.CurrentStep.Name);
                return;
            }

            // Generate sub-steps from data
            var subSteps = dataItems
                .Select((item, index) => config.StepFactory(item, index))
                .ToList();

            context.CurrentStep.Branches.Add(new FlowBranch
            {
                IsDefault = true,
                Steps = subSteps
            });

            _logger.LogInformation("Generated {SubStepCount} dynamic sub-steps for step {StepName} using strategy {Strategy}",
                subSteps.Count, context.CurrentStep.Name, config.ExecutionStrategy);

            // Execute based on strategy
            switch (config.ExecutionStrategy)
            {
                case ExecutionStrategy.Sequential:
                    await ExecuteSequential(context, subSteps, cancellationToken);
                    break;

                case ExecutionStrategy.Parallel:
                    await ExecuteParallel(context, subSteps, cancellationToken);
                    break;

                case ExecutionStrategy.RoundRobin:
                    await ExecuteRoundRobin(context, subSteps, cancellationToken);
                    break;

                case ExecutionStrategy.Batched:
                    await ExecuteBatched(context, subSteps, config, cancellationToken);
                    break;

                case ExecutionStrategy.PriorityBased:
                    await ExecutePriorityBased(context, subSteps, cancellationToken);
                    break;
            }
        }

        private async Task ExecuteSequential(FlowExecutionContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
        {
            foreach (var subStep in subSteps)
            {
                await ExecuteStepAsync(context.Flow, subStep, cancellationToken);
                await context.Flow.PersistAsync();
                await _notificationService.NotifyFlowStatusChanged(context.Flow);
                await _notificationService.NotifyStepStatusChanged(context.Flow, context.CurrentStep);
            }
        }

        private async Task ExecuteParallel(FlowExecutionContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
        {
            var tasks = subSteps.Select(subStep => ExecuteStepAsync(context.Flow, subStep, cancellationToken));
            await Task.WhenAll(tasks);
            await context.Flow.PersistAsync();
            await _notificationService.NotifyFlowStatusChanged(context.Flow);
            await _notificationService.NotifyStepStatusChanged(context.Flow, context.CurrentStep);
        }

        private async Task ExecuteRoundRobin(FlowExecutionContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
        {
            // Group by resource (e.g., exchange) and distribute evenly
            var resourceGroups = subSteps
                .GroupBy(s => s.ResourceGroup ?? "default")
                .ToList();

            var tasks = resourceGroups.Select(async group =>
            {
                foreach (var subStep in group)
                {
                    await ExecuteStepAsync(context.Flow, subStep, cancellationToken);
                    await context.Flow.PersistAsync();
                    await _notificationService.NotifyFlowStatusChanged(context.Flow);
                    await _notificationService.NotifyStepStatusChanged(context.Flow, context.CurrentStep);
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task ExecuteBatched(FlowExecutionContext context, List<FlowSubStep> subSteps, DynamicBranchingConfig config, CancellationToken cancellationToken)
        {
            var batchSize = config.MaxConcurrency;
            var batches = subSteps
                .Select((step, index) => new { step, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.step).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                await ExecuteParallel(context, batch, cancellationToken);

                if (config.BatchDelay > TimeSpan.Zero)
                {
                    await Task.Delay(config.BatchDelay, cancellationToken);
                }
            }
        }

        private async Task ExecutePriorityBased(FlowExecutionContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
        {
            // Sort by priority (higher number = higher priority)
            var sortedSteps = subSteps.OrderByDescending(s => s.Priority).ToList();
            await ExecuteSequential(context, sortedSteps, cancellationToken);
        }

        private async Task ExecuteStaticBranches(FlowExecutionContext context, List<FlowBranch> branches, CancellationToken cancellationToken)
        {
            foreach (var branch in branches)
            {
                bool shouldExecute = branch.IsDefault || (branch.Condition?.Invoke(context) == true);

                if (shouldExecute)
                {
                    foreach (var subStep in branch.Steps)
                    {
                        await ExecuteStepAsync(context.Flow, subStep, cancellationToken);
                    }

                    break; // Only execute first matching branch
                }
            }
        }

        private Task TriggerFlow(FlowExecutionContext context, Type flowType)
        {
            _logger.LogInformation("Triggering flow {FlowType} from step {StepName}", flowType.Name, context.CurrentStep.Name);

            // Use the scope factory instead of the potentially disposed service provider
            using var scope = _scopeFactory.CreateScope();

            var flow = Flow.Builder(scope.ServiceProvider)
                .ForUser(context.State.UserId)
                .WithCorrelation($"{context.State.CorrelationId}:triggered:{flowType.Name}")
                .TriggeredBy(context.Flow.Id)
                .Build(flowType);

            // Execute the flow
            return flow.ExecuteAsync(); // Fire and forget
        }

        /// <summary>
        /// Generates an idempotency key for a step based on its context and data dependencies
        /// </summary>
        private string GenerateStepIdempotencyKey<T>(T flow, FlowStep step, FlowExecutionContext context) where T : Flow
        {
            var keyComponents = new StringBuilder();

            // Add flow and step identifiers
            keyComponents.Append($"flow:{flow.State.FlowId}:step:{step.Name}");

            // Add user context for isolation
            if (!string.IsNullOrEmpty(flow.State.UserId))
            {
                keyComponents.Append($":user:{flow.State.UserId}");
            }

            // Generate content hash from data dependencies
            if (step.DataDependencies?.Any() == true)
            {
                var dependencyData = new Dictionary<string, object>();

                foreach (var dependency in step.DataDependencies)
                {
                    if (context.State.Data.TryGetValue(dependency.Key, out var value))
                    {
                        dependencyData[dependency.Key] = value;
                    }
                }

                if (dependencyData.Count != 0)
                {
                    var contentHash = GenerateContentHash(dependencyData);
                    keyComponents.Append($":hash:{contentHash}");
                }
            }

            return keyComponents.ToString();
        }

        /// <summary>
        /// Generates a SHA256 hash from the given data dependencies
        /// </summary>
        private string GenerateContentHash(Dictionary<string, object> data)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
                return Convert.ToBase64String(hashBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate content hash for step data dependencies");
                // Return a fallback hash based on the data count and types
                return $"fallback:{data.Count}:{string.Join(",", data.Keys.OrderBy(k => k))}";
            }
        }
    }
}
