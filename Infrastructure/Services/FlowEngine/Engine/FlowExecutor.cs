using Application.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services.FlowEngine.Engine
{
    public class FlowExecutor : IFlowExecutor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FlowExecutor> _logger;
        private readonly IFlowPersistence _persistence;
        private readonly IIdempotencyService _idempotency;

        public FlowExecutor(IServiceProvider serviceProvider, ILogger<FlowExecutor> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _persistence = serviceProvider.GetRequiredService<IFlowPersistence>();
            _idempotency = serviceProvider.GetRequiredService<IIdempotencyService>();
        }

        public async Task<FlowResult<T>> ExecuteAsync<T>(T flow, CancellationToken cancellationToken) where T : FlowDefinition
        {
            flow.Initialize();
            
            try
            {
                flow.Status = FlowStatus.Running;
                flow.StartedAt = DateTime.UtcNow;

                for (int i = flow.CurrentStepIndex; i < flow.Steps.Count; i++)
                {
                    var step = flow.Steps[i];
                    flow.CurrentStepName = step.Name;
                    flow.CurrentStepIndex = flow.Steps.IndexOf(step);

                    await ExecuteStepAsync(flow, step, cancellationToken);

                    await _persistence.SaveFlowStateAsync(flow);

                    if (flow.Status == FlowStatus.Paused || cancellationToken.IsCancellationRequested)
                        break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return FlowResult<T>.Cancelled(flow, "Flow execution was cancelled");
                }

                if (flow.Status == FlowStatus.Paused)
                {
                    return FlowResult<T>.Paused(flow, "Flow is paused");
                }

                flow.Status = FlowStatus.Completed;
                flow.CompletedAt = DateTime.UtcNow;

                await _persistence.SaveFlowStateAsync(flow);

                return FlowResult<T>.Success(flow, "Flow completed successfully");
            }
            catch (Exception ex)
            {
                flow.Status = FlowStatus.Failed;
                flow.LastError = ex;
                flow.CompletedAt = DateTime.UtcNow;

                await _persistence.SaveFlowStateAsync(flow);

                return FlowResult<T>.Failure(flow, ex.Message, ex);
            }
        }

        private async Task ExecuteStepAsync<T>(T flow, FlowStep step, CancellationToken cancellationToken) where T : FlowDefinition
        {
            step.Status = StepStatus.InProgress;

            var context = new FlowContext
            {
                Flow = flow,
                CurrentStep = step,
                CancellationToken = cancellationToken,
                Services = _serviceProvider
            };

            // Check conditional execution
            if (step.Condition != null && step.Condition?.Invoke(context) != true)
            {
                step.Status = StepStatus.Skipped;
                await _persistence.SaveFlowStateAsync(flow);
                return; // Skip step execution
            }

            _logger.LogInformation("Executing step {StepName} in flow {FlowId}", step.Name, flow.FlowId);

            // Check data dependencies
            foreach (var dependency in step.DataDependencies)
            {
                if (!context.Flow.Data.ContainsKey(dependency.Key))
                {
                    step.Status = StepStatus.Failed;
                    throw new FlowExecutionException($"Missing required data for step {step.Name}: {dependency.Key}");
                }
                if (context.Flow.Data[dependency.Key].Type != dependency.Value.FullName)
                {
                    step.Status = StepStatus.Failed;
                    throw new FlowExecutionException($"Invalid required data type for step {step.Name}: {dependency.Value.Name}");
                }
            }

            // NEW: Check if step should pause
            if (step.PauseCondition != null)
            {
                var pauseCondition = step.PauseCondition(context);
                if (pauseCondition.ShouldPause)
                {
                    step.Status = StepStatus.Paused;
                    await PauseFlowAsync(flow, step, pauseCondition);
                    return; // Exit step execution as flow is paused
                }
            }

            // Check if step is idempotent
            string? idempotencyKey = null;

            if (step.IsIdempotent)
            {
                // Generate idempotency key based on step context and data dependencies
                idempotencyKey = GenerateStepIdempotencyKey(flow, step, context);

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
                            context.Flow.Data[kvp.Key] = kvp.Value;
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
                    var result = await step.ExecuteAsync(context);

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
                            context.Flow.Data[kvp.Key] = kvp.Value;
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
                    throw;
                }
            }

            // Handle dynamic sub-branching
            if (step.DynamicBranching != null)
            {
                await ExecuteDynamicSubSteps(context, step.DynamicBranching, cancellationToken);
            }

            // Handle static conditional branching
            if (step.Branches.Count > 0)
            {
                await ExecuteStaticBranches(context, step.Branches, cancellationToken);
            }

            // Trigger other flows if configured
            if (step.TriggeredFlows != null && step.TriggeredFlows.Count > 0)
            {
                try
                {
                    var tasks = step.TriggeredFlows.Select(flowType => TriggerFlow(context, flowType));
                    Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    // Triggering external flows should not fail this flow.
                    _logger.LogWarning(ex, "Failed to trigger flow");
                }
                
            }

            // Handle jump to another step if configured
            if (step.JumpTo != null && !string.IsNullOrWhiteSpace(step.JumpTo))
            {
                var targetIndex = flow.Steps.FindIndex(s => s.Name == step.JumpTo);
                if (targetIndex == -1)
                {
                    step.Status = StepStatus.Failed;
                    throw new FlowExecutionException($"Step {step.Name} attempted to jump to unknown step {step.JumpTo}");
                }
                flow.CurrentStepIndex = targetIndex - 1; // -1 because the main loop will increment it
            }

            step.Status = StepStatus.Completed;
        }

        /// <summary>
        /// Pause the flow with the specified condition
        /// </summary>
        private async Task PauseFlowAsync<T>(T flow, FlowStep step, PauseCondition pauseCondition) where T : FlowDefinition
        {
            flow.Status = FlowStatus.Paused;
            flow.PausedAt = DateTime.UtcNow;
            flow.PauseReason = pauseCondition.Reason;
            flow.PauseMessage = pauseCondition.Message;
            flow.PauseData = pauseCondition.Data.ToSafe();

            // Set resume configuration from step or pause condition
            flow.ActiveResumeConfig = pauseCondition.ResumeConfig ?? step.ResumeConfig;

            _logger.LogInformation("Flow {FlowId} paused at step {StepName}: {Reason} - {Message}",
                flow.FlowId, step.Name, pauseCondition.Reason, pauseCondition.Message);

            // Add pause event to timeline
            flow.Events.Add(new FlowEvent
            {
                FlowId = flow.FlowId.ToString(),
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
            await _persistence.SaveFlowStateAsync(flow);

            // If there's an auto-resume condition, register it
            if (flow.ActiveResumeConfig?.AutoResumeCondition != null)
            {
                var resumeCondition = new ResumeCondition
                {
                    FlowId = flow.FlowId,
                    Condition = flow.ActiveResumeConfig.AutoResumeCondition,
                    CheckInterval = flow.ActiveResumeConfig.ConditionCheckInterval,
                    NextCheck = DateTime.UtcNow.Add(flow.ActiveResumeConfig.ConditionCheckInterval)
                };

                await _persistence.SetResumeConditionAsync(flow.FlowId, resumeCondition);
            }
        }

        /// <summary>
        /// Resume flow internally (update local state)
        /// </summary>
        public async Task ResumeFlowAsync<T>(T flow, string reason) where T : FlowDefinition
        {
            var success = await _persistence.ResumeFlowAsync(flow.FlowId, ResumeReason.Condition, "system", reason);

            if (!success)
            {
                _logger.LogWarning("Failed to resume flow {FlowId} - it may not be in a paused state", flow.FlowId);
                return;
            }

            flow.Status = FlowStatus.Running;
            flow.PauseReason = null;
            flow.PausedAt = null;
            flow.PauseMessage = null;
            flow.Events.Add(new FlowEvent
            {
                EventId = Guid.NewGuid().ToString(),
                FlowId = flow.FlowId.ToString(),
                EventType = "FlowResumed",
                Description = $"Flow resumed by {"system"} (reason: {reason})",
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object>
                {
                    { "resumeReason", reason.ToString() },
                    { "resumedBy", "system" }
                }.ToSafe()
            });

            var pausedStep = flow.Steps.ElementAtOrDefault(flow.CurrentStepIndex);
            if (pausedStep != null && pausedStep.Status == StepStatus.Paused)
            {
                pausedStep.PauseCondition = null; // Clear pause condition to avoid re-pausing
            }

            await _persistence.SaveFlowStateAsync(flow);

            await ExecuteAsync(flow, CancellationToken.None); // Fire and forget

            _logger.LogInformation("Flow {FlowId} resumed internally: {Reason}", flow.FlowId, reason);
        }

        /// <summary>
        /// Execute dynamic sub-steps based on runtime data
        /// </summary>
        private async Task ExecuteDynamicSubSteps(FlowContext context, DynamicBranchingConfig config, CancellationToken cancellationToken)
        {
            // Get data items that will become sub-steps
            var dataItems = config.DataSelector(context).ToList();

            if (!dataItems.Any())
            {
                _logger.LogInformation("No data items found for dynamic branching in step {StepName}", context.CurrentStep.Name);
                return;
            }

            // Generate sub-steps from data
            var subSteps = dataItems
                .Select((item, index) => config.StepFactory(item, index))
                .ToList();

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

        private async Task ExecuteSequential(FlowContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
        {
            foreach (var subStep in subSteps)
            {
                await ExecuteStepAsync(context.Flow, subStep, cancellationToken);
            }
        }

        private async Task ExecuteParallel(FlowContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
        {
            var tasks = subSteps.Select(subStep => ExecuteStepAsync(context.Flow, subStep, cancellationToken));
            await Task.WhenAll(tasks);
        }

        private async Task ExecuteRoundRobin(FlowContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
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
                    // Small delay between steps in same resource group
                    await Task.Delay(100, cancellationToken);
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task ExecuteBatched(FlowContext context, List<FlowSubStep> subSteps, DynamicBranchingConfig config, CancellationToken cancellationToken)
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

        private async Task ExecutePriorityBased(FlowContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
        {
            // Sort by priority (higher number = higher priority)
            var sortedSteps = subSteps.OrderByDescending(s => s.Priority).ToList();
            await ExecuteSequential(context, sortedSteps, cancellationToken);
        }

        private async Task ExecuteStaticBranches(FlowContext context, List<FlowBranch> branches, CancellationToken cancellationToken)
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

        private Task TriggerFlow(FlowContext context, Type flowType)
        {
            _logger.LogInformation("Triggering flow {FlowType} from step {StepName}", flowType.Name, context.CurrentStep.Name);

            // Get flow instance from DI container
            var flow = (FlowDefinition)_serviceProvider.GetRequiredService(flowType);

            // Initialize the flow
            flow.FlowId = Guid.NewGuid();
            flow.UserId = context.Flow.UserId;
            flow.CorrelationId = $"{context.Flow.CorrelationId}:triggered:{flowType.Name}";
            flow.CreatedAt = DateTime.UtcNow;
            flow.Status = FlowStatus.Initializing;

            // Execute the flow
            var executor = _serviceProvider.GetRequiredService<IFlowExecutor>();
            return executor.ExecuteAsync(flow, context.CancellationToken);
        }

        /// <summary>
        /// Generates an idempotency key for a step based on its context and data dependencies
        /// </summary>
        private string GenerateStepIdempotencyKey<T>(T flow, FlowStep step, FlowContext context) where T : FlowDefinition
        {
            var keyComponents = new StringBuilder();

            // Add flow and step identifiers
            keyComponents.Append($"flow:{flow.FlowId}:step:{step.Name}");

            // Add user context for isolation
            if (!string.IsNullOrEmpty(flow.UserId))
            {
                keyComponents.Append($":user:{flow.UserId}");
            }

            // Generate content hash from data dependencies
            if (step.DataDependencies?.Any() == true)
            {
                var dependencyData = new Dictionary<string, object>();

                foreach (var dependency in step.DataDependencies)
                {
                    if (context.Flow.Data.TryGetValue(dependency.Key, out var value))
                    {
                        dependencyData[dependency.Key] = value;
                    }
                }

                if (dependencyData.Any())
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
