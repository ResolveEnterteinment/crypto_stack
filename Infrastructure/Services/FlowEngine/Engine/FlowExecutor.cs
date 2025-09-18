using Application.Interfaces;
using Domain.DTOs.Flow;
using Infrastructure.Hubs;
using Infrastructure.Services.FlowEngine.Core.Builders;
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
        private readonly ILogger<FlowExecutor> _logger;
        private readonly IIdempotencyService _idempotency;
        private readonly IFlowSecurity _security;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IFlowNotificationService _notificationService;

        public FlowExecutor(
            IServiceProvider serviceProvider, 
            ILogger<FlowExecutor> logger,
            IServiceScopeFactory scopeFactory,
            IHubContext<FlowHub> hubContext)
        {
            _logger = logger;
            _security = serviceProvider.GetRequiredService<IFlowSecurity>();
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

                flow.State.Events.Add(new FlowEvent
                {
                    FlowId = flow.State.FlowId,
                    EventType = "FlowStarted",
                    Description = $"Flow execution started.",
                    Data = new Dictionary<string, object>
                    {
                        { "startedAt", flow.State.StartedAt }
                    }.ToSafe(),
                });

                // Send initial status update
                await _notificationService.NotifyFlowStatusChanged(flow);

                while (flow.State.CurrentStepIndex < flow.Definition.Steps.Count && !cancellationToken.IsCancellationRequested)
                {
                    var step = flow.Definition.Steps[flow.State.CurrentStepIndex];

                    flow.Context.CurrentStep = step;
                    flow.State.CurrentStepName = step.Name;
                    flow.State.CurrentStepIndex = flow.Definition.Steps.IndexOf(step);

                    await ExecuteStepAsync(flow.Context, step, cancellationToken);

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
                    return FlowExecutionResult.Paused(flow.State.FlowId, "Flow is paused", flow.State.PauseData?.ToValue());
                }

                flow.State.Status = FlowStatus.Completed;
                flow.State.CompletedAt = DateTime.UtcNow;

                await flow.PersistAsync();

                // Send completion notification
                await _notificationService.NotifyFlowStatusChanged(flow);

                flow.State.Events.Add(new FlowEvent
                {
                    FlowId = flow.State.FlowId,
                    EventType = "FlowCompleted",
                    Description = $"Flow completed successfully.",
                    Data = new Dictionary<string, object>
                    {
                        { "totalSteps", flow.Definition.Steps.Count },
                        { "completedAt", flow.State.CompletedAt }
                    }.ToSafe(),
                });

                return FlowExecutionResult.Success(flow.Id, "Flow completed successfully", data: flow.Context.CurrentStep.Result?.Data?.ToValue());
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

        private async Task ExecuteStepAsync(FlowExecutionContext stepContext, FlowStep step, CancellationToken cancellationToken)
        {
            // Check for cancellation before starting step execution
            if (cancellationToken.IsCancellationRequested)
            {
                step.Status = StepStatus.Cancelled;
                return;
            }

            var flow = stepContext.Flow;

            //add stop watch
            step.StartedAt = DateTime.UtcNow;

            try
            {
                // Check step dependencies
                foreach (var dependency in step.StepDependencies)
                {
                    var depStep = flow.Definition.Steps.FirstOrDefault(s => s.Name == dependency);
                    if (depStep == null || depStep.Status != StepStatus.Completed)
                    {
                        step.Status = StepStatus.Skipped;
                        return; // Skip execution if dependency not met
                    }
                }

                // Check role requirements
                if (step.RequiredRoles != null && step.RequiredRoles.Count > 0)
                {
                    var userId = flow.State.UserId ?? "system";
                    var securityService = flow.GetService<IFlowSecurity>();

                    // Check specific role requirements
                    foreach (var requiredRole in step.RequiredRoles)
                    {
                        var hasRole = await securityService.UserHasRoleAsync(userId, requiredRole);
                        {
                            step.Status = StepStatus.Failed;
                            throw new FlowSecurityException($"User {userId} lacks required role '{requiredRole}' for step {step.Name}");
                        }
                    }

                    _logger.LogInformation("Role validation passed for user {UserId} on step {StepName} with roles: {Roles}");
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

                // Check if step should pause
                if (step.PauseCondition != null)
                {
                    var pauseCondition = await step.PauseCondition(flow.Context);
                    if (pauseCondition.ShouldPause)
                    {
                        step.Status = StepStatus.Paused;

                        step.Result = StepResult.Paused(pauseCondition.Message, pauseCondition.Data);

                        await PauseInternalAsync(flow, pauseCondition);

                        return; // Exit step execution as flow is paused
                    }
                }

                _logger.LogInformation("Executing step {StepName} in flow {FlowId}", step.Name, flow.State.FlowId);

                // Check conditional execution
                if (step.Condition != null && step.Condition?.Invoke(flow.Context) != true)
                {
                    step.Status = StepStatus.Skipped;

                    await flow.PersistAsync();

                    await _notificationService.NotifyStepStatusChanged(flow, step);
                    return; // Skip step execution

                }

                // Check if step is idempotent
                string? idempotencyKey = null;

                if (step.IsIdempotent)
                {
                    // Generate idempotency key based on step context and data dependencies
                    idempotencyKey = step.IdempotencyKey ?? step.IdempotencyKeyFactory?.Invoke(flow.Context) ?? GenerateStepIdempotencyKey(flow, step, flow.Context);

                    // Check if this step was already executed successfully
                    var (resultExists, cachedResult) = await _idempotency.GetResultAsync<StepResult>(idempotencyKey);

                    if (resultExists && cachedResult != null)
                    {
                        _logger.LogInformation("Idempotent step {StepName} already executed successfully, returning cached result", step.Name);

                        step.Result = StepResult.ConcurrencyConflict(data: cachedResult);

                        // Skip execution and continue to next step
                        return;
                    }
                }

                // Execute main step logic
                if (step.ExecuteAsync != null)
                {
                    try
                    {
                        var result = await step.ExecuteAsync(stepContext);

                        step.Result = result;

                        if (result == null || !result.IsSuccess)
                        {
                            throw new FlowExecutionException($"Step {step.Name} failed: {result?.Message ?? "Step result returned null,"}");
                        }

                        if (step.IsIdempotent)
                        {
                            // Store the successful result for future idempotency checks
                            await _idempotency.StoreResultAsync(idempotencyKey!, result, TimeSpan.FromHours(24));
                        }
                    }
                    catch (Exception ex)
                    {
                        step.Status = StepStatus.Failed;
                        step.Result = StepResult.Failure(ex.Message, ex);

                        if (step.AllowFailure)
                        {
                            _logger.LogWarning("Step {StepName} failed but is allowed to fail, continuing flow", step.Name);
                        }
                        else
                        {
                            _logger.LogError("Step {StepName} failed, aborting flow: {Message}", step.Name, ex.Message);
                            throw; // Rethrow to be caught by outer flow execution handler
                        }
                    }
                }

                if (step.Status == StepStatus.Cancelled)
                {
                    // Exit if flow was cancelled during step execution
                    await CancelFlowAsync(flow, "Step execution cancelled", cancellationToken);
                    return;
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

                if (step.Status == StepStatus.Cancelled)
                {
                    // Exit if flow was cancelled during step execution
                    await CancelFlowAsync(flow, "Step execution cancelled", cancellationToken);
                    return;
                }

                // Trigger other flows if configured
                if (step.TriggeredFlows != null && step.TriggeredFlows.Count > 0)
                {
                    try
                    {
                        var tasks = step.TriggeredFlows.Select(triggeredFlowData => TriggerFlow(flow.Context, triggeredFlowData, cancellationToken));
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
            catch (Exception ex)
            {
                step.Status = StepStatus.Failed;
                step.Error = SerializableError.FromException(ex);
                step.Result = StepResult.Failure(ex.Message, ex);
                throw;
            }
            finally
            {
                step.CompletedAt = DateTime.UtcNow;
            }
            
        }

        /// <summary>
        /// Pause the flow with the specified condition
        /// </summary>
        private async Task PauseInternalAsync(Flow flow, PauseCondition pauseCondition)
        {
            flow.State.Status = FlowStatus.Paused;
            flow.State.PausedAt = DateTime.UtcNow;
            flow.State.PauseReason = pauseCondition.Reason;
            flow.State.PauseMessage = pauseCondition.Message;
            flow.State.PauseData = SafeObject.FromValue(pauseCondition.Data);

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

            return;
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

        public async Task<FlowExecutionResult> CancelFlowAsync(Flow flow, string reason, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return FlowExecutionResult.Failure(flow.State.FlowId, "Failed to cancel flow. Cancellation token can not be cancelled.");
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.Cancel();

            if (cts.IsCancellationRequested.Equals(true))
                _logger.LogWarning($"Flow cancellation requested!");

            flow.State.Status = FlowStatus.Cancelled;
            flow.State.CancelledAt = DateTime.UtcNow;
            flow.State.CancelReason = reason;

            flow.State.Events.Add(new FlowEvent
            {
                EventId = Guid.NewGuid(),
                FlowId = flow.State.FlowId,
                EventType = "FlowCancelled",
                Description = $"Flow cancelled by {"system"} (reason: {reason})",
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object>
                {
                    { "cancelReason", reason.ToString() },
                    { "cancelledBy", "system" }
                }.ToSafe()
            });

            await flow.PersistAsync();

            return FlowExecutionResult.Cancelled(flow.State.FlowId, reason);
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

            var branches = new List<FlowBranch>();

            // Generate branches from data
            dataItems
                .ForEach(item =>
                {
                    var index = dataItems.IndexOf(item);

                    var builder = new FlowBranchBuilder(context.CurrentStep);
                    var branch = config.BranchFactory(builder, item, index);

                    branches.Add(branch);
                });

            _logger.LogInformation("Generated {SubStepCount} dynamic sub-steps for step {StepName} using strategy {Strategy}",
                dataItems.Count, context.CurrentStep.Name, config.ExecutionStrategy);

            // Execute based on strategy
            switch (config.ExecutionStrategy)
            {
                case ExecutionStrategy.Sequential:
                    await ExecuteSequential(context, branches, cancellationToken);
                    break;

                case ExecutionStrategy.Parallel:
                    await ExecuteParallel(context, branches, cancellationToken);
                    break;

                case ExecutionStrategy.RoundRobin:
                    await ExecuteRoundRobin(context, branches, cancellationToken);
                    break;

                case ExecutionStrategy.Batched:
                    await ExecuteBatched(context, branches, config, cancellationToken);
                    break;

                case ExecutionStrategy.PriorityBased:
                    await ExecutePriorityBased(context, branches, cancellationToken);
                    break;
            }
            await context.Flow.PersistAsync();
        }

        #region Dynamic Sub-Step Execution Strategies
        private async Task ExecuteSequential(FlowExecutionContext context, List<FlowBranch> branches, CancellationToken cancellationToken)
        {
            
            foreach (var branch in branches)
            {
                foreach (var subStep in branch.Steps)
                {
                    var stepContext = new FlowExecutionContext
                    {
                        Flow = context.Flow,
                        CurrentStep = subStep,
                        Services = context.Services
                    };

                    await ExecuteStepAsync(stepContext, subStep, cancellationToken);

                    await _notificationService.NotifyFlowStatusChanged(context.Flow);
                    await _notificationService.NotifyStepStatusChanged(context.Flow, context.CurrentStep);
                }
            }
        }

        private async Task ExecuteParallel(FlowExecutionContext context, List<FlowBranch> branches, CancellationToken cancellationToken)
        {
            var tasks = branches.Select(branch =>
            {
                return Task.Run(async () =>
                {
                    foreach (var subStep in branch.Steps)
                    {
                        var stepContext = new FlowExecutionContext
                        {
                            Flow = context.Flow,
                            CurrentStep = subStep,
                            Services = context.Services
                        };

                        await ExecuteStepAsync(stepContext, subStep, cancellationToken);
                    }
                });
            });

            await Task.WhenAll(tasks);

            await _notificationService.NotifyFlowStatusChanged(context.Flow);
            await _notificationService.NotifyStepStatusChanged(context.Flow, context.CurrentStep);
        }

        private async Task ExecuteRoundRobin(FlowExecutionContext context, List<FlowBranch> branches, CancellationToken cancellationToken)
        {
            // Group by resource (e.g., exchange) and distribute evenly
            var resourceGroups = branches
                .GroupBy(b => b.ResourceGroup ?? "default")
                .ToList();

            var tasks = resourceGroups.Select(async group =>
            {
                foreach (var branch in group)
                {
                    foreach (var subStep in branch.Steps)
                    {
                        var stepContext = new FlowExecutionContext
                        {
                            Flow = context.Flow,
                            CurrentStep = subStep,
                            Services = context.Services
                        };

                        await ExecuteStepAsync(stepContext, subStep, cancellationToken);

                        await _notificationService.NotifyFlowStatusChanged(context.Flow);
                        await _notificationService.NotifyStepStatusChanged(context.Flow, context.CurrentStep);
                    }
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task ExecuteBatched(FlowExecutionContext context, List<FlowBranch> branches, DynamicBranchingConfig config, CancellationToken cancellationToken)
        {
            /*
            var batchSize = config.MaxConcurrency;
            var batches = branch.Steps
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
            */

            throw new NotImplementedException("Batched execution strategy is not implemented yet.");
        }

        private async Task ExecutePriorityBased(FlowExecutionContext context, List<FlowBranch> branches, CancellationToken cancellationToken)
        {
            // Sort by priority (higher number = higher priority)
            var sortedBranches = branches.OrderByDescending(b => b.Priority).ToList();
            await ExecuteSequential(context, sortedBranches, cancellationToken);
        }
        #endregion

        private async Task ExecuteStaticBranches(FlowExecutionContext context, List<FlowBranch> branches, CancellationToken cancellationToken)
        {
            foreach (var branch in branches)
            {
                bool shouldExecute = branch.IsDefault || (branch.Condition?.Invoke(context) == true);

                if (shouldExecute)
                {
                    foreach (var subStep in branch.Steps)
                    {
                        var stepContext = new FlowExecutionContext
                        {
                            Flow = context.Flow,
                            CurrentStep = subStep,
                            Services = context.Services
                        };

                        await ExecuteStepAsync(stepContext, subStep, cancellationToken);
                    }

                    break; // Only execute first matching branch
                }
            }
        }

        private Task TriggerFlow(FlowExecutionContext context, TriggeredFlowData triggeredFlowData, CancellationToken? cancellationToken = null)
        {
            if (string.IsNullOrEmpty(triggeredFlowData.Type))
            {
                _logger.LogError("TriggeredFlowData.Type is null or empty for step {StepName}", context.CurrentStep.Name);
                return Task.CompletedTask;
            }

            var flowType = Type.GetType(triggeredFlowData.Type);

            if (flowType == null)
            {
                _logger.LogWarning("Failed to resolve type {TypeName} using Type.GetType(), attempting alternative resolution", triggeredFlowData.Type);

                // Try to find the type in currently loaded assemblies
                flowType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.AssemblyQualifiedName == triggeredFlowData.Type || t.FullName == triggeredFlowData.Type);
            }

            if (flowType == null)
            {
                _logger.LogError("Cannot resolve flow type: {TypeName} for step {StepName}. Flow triggering aborted.",
                    triggeredFlowData.Type, context.CurrentStep.Name);
                return Task.CompletedTask;
            }

            if (context?.CurrentStep?.Name == null)
            {
                _logger.LogError("Context or CurrentStep or CurrentStep.Name is null when triggering flow {FlowType}", flowType.Name);
                return Task.CompletedTask;
            }

            _logger.LogInformation("Triggering flow {FlowType} from step {StepName}", flowType.Name, context.CurrentStep.Name);

            // Use the scope factory instead of the potentially disposed service provider
            using var scope = _scopeFactory.CreateScope();

            // Pass the current flow's cancellation token to create a linked cancellation context
            var effectiveToken = cancellationToken ?? context.Flow.CancellationToken;

            var initialData = triggeredFlowData.InitialDataFactory?.Invoke(context) ?? [];

            var flow = Flow.Builder(scope.ServiceProvider, effectiveToken)
                .ForUser(context.State.UserId, context.State.UserEmail)
                .WithCorrelation($"{context.State.CorrelationId}:triggered:{flowType.Name}")
                .WithData(initialData)
                .TriggeredBy(new TriggeredFlowData
                {
                    Type = context.State.FlowType,
                    FlowId = context.State.FlowId,
                    TiggeredByStep = context.CurrentStep.Name
                })
                .Build(flowType);

            triggeredFlowData.FlowId = flow.Id;
            triggeredFlowData.TiggeredByStep = context.CurrentStep.Name;

            var triggeredFlow = context.CurrentStep.TriggeredFlows.Find(tf => tf.Type == triggeredFlowData.Type);
            if (triggeredFlow != null)
            {
                triggeredFlow.FlowId = triggeredFlowData.FlowId;
                triggeredFlow.TiggeredByStep = triggeredFlowData.TiggeredByStep;
            }

            context.Flow.State.Events.Add(new FlowEvent
            {
                FlowId = context.Flow.Id,
                EventType = "FlowTriggered",
                Description = $"Triggered flow {flowType.Name} with id {flow.State.FlowId}",
                Data = new Dictionary<string, object>
        {
            { "triggeredFlowId", flow.State.FlowId },
            { "triggeredFlowType", flowType.Name },
            { "triggeredByStep", context.CurrentStep.Name }
        }.ToSafe()
            });

            // Execute the flow with linked cancellation token
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
            if (step.DataDependencies?.Count > 0)
            {
                var dependencyData = new Dictionary<string, object>();

                foreach (var dependency in step.DataDependencies)
                {
                    if (context.State.Data.TryGetValue(dependency.Key, out var value))
                    {
                        dependencyData[dependency.Key] = value;
                    }
                }
            }

            return keyComponents.ToString();
        }
    }
}
