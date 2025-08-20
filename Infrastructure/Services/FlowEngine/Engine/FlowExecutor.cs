using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Engine
{
    public class FlowExecutor : IFlowExecutor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FlowExecutor> _logger;

        public FlowExecutor(IServiceProvider serviceProvider, ILogger<FlowExecutor> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<FlowResult<T>> ExecuteAsync<T>(T flow, CancellationToken cancellationToken) where T : FlowDefinition
        {
            flow.Initialize();
            flow.Status = FlowStatus.Running;
            flow.StartedAt = DateTime.UtcNow;

            try
            {
                foreach (var step in flow.Steps)
                {
                    await ExecuteStepAsync(flow, step, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }

                flow.Status = FlowStatus.Completed;
                flow.CompletedAt = DateTime.UtcNow;

                return FlowResult<T>.Success(flow, "Flow completed successfully");
            }
            catch (Exception ex)
            {
                flow.Status = FlowStatus.Failed;
                flow.LastError = ex;
                flow.CompletedAt = DateTime.UtcNow;

                return FlowResult<T>.Failure(flow, ex.Message, ex);
            }
        }

        private async Task ExecuteStepAsync<T>(T flow, FlowStep step, CancellationToken cancellationToken) where T : FlowDefinition
        {
            var context = new FlowContext
            {
                Flow = flow,
                CurrentStep = step,
                CancellationToken = cancellationToken,
                Services = _serviceProvider
            };

            flow.CurrentStepName = step.Name;
            _logger.LogInformation("Executing step {StepName} in flow {FlowId}", step.Name, flow.FlowId);

            // Execute main step logic
            if (step.ExecuteAsync != null)
            {
                var result = await step.ExecuteAsync(context);
                if (!result.IsSuccess)
                {
                    throw new FlowExecutionException($"Step {step.Name} failed: {result.Message}");
                }
            }

            // NEW: Check if step should pause
            if (step.PauseCondition != null)
            {
                var pauseCondition = step.PauseCondition(context);
                if (pauseCondition.ShouldPause)
                {
                    await PauseFlowAsync(flow, step, pauseCondition);
                    return; // Exit execution - flow is now paused
                }
            }

            // Handle dynamic sub-branching
            if (step.DynamicBranching != null)
            {
                await ExecuteDynamicSubSteps(context, step.DynamicBranching, cancellationToken);
            }

            // Handle static conditional branching
            if (step.Branches?.Any() == true)
            {
                await ExecuteStaticBranches(context, step.Branches, cancellationToken);
            }

            // Trigger other flows if configured
            if (step.TriggeredFlow != null)
            {
                await TriggerFlow(context, step.TriggeredFlow);
            }
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
            flow.PauseData = pauseCondition.Data;

            // Set resume configuration from step or pause condition
            flow.ActiveResumeConfig = pauseCondition.ResumeConfig ?? step.ResumeConfig;

            _logger.LogInformation("Flow {FlowId} paused at step {StepName}: {Reason} - {Message}",
                flow.FlowId, step.Name, pauseCondition.Reason, pauseCondition.Message);

            // Save paused state to persistence
            var persistence = _serviceProvider.GetRequiredService<IFlowPersistence>();
            await persistence.SaveFlowStateAsync(flow);

            // Add pause event to timeline
            flow.Events.Add(new FlowEvent
            {
                FlowId = flow.FlowId,
                EventType = "FlowPaused",
                Description = $"Flow paused: {pauseCondition.Message}",
                Data = new Dictionary<string, object>
                {
                    ["Reason"] = pauseCondition.Reason.ToString(),
                    ["StepName"] = step.Name,
                    ["PauseData"] = pauseCondition.Data
                }
            });

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

                await persistence.SetResumeConditionAsync(flow.FlowId, resumeCondition);
            }
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
                await ExecuteSubStep(context, subStep, cancellationToken);
            }
        }

        private async Task ExecuteParallel(FlowContext context, List<FlowSubStep> subSteps, CancellationToken cancellationToken)
        {
            var tasks = subSteps.Select(subStep => ExecuteSubStep(context, subStep, cancellationToken));
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
                    await ExecuteSubStep(context, subStep, cancellationToken);
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

        private async Task ExecuteSubStep(FlowContext context, FlowSubStep subStep, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Executing sub-step {SubStepName} for {SourceData}",
                    subStep.Name, subStep.SourceData?.ToString());

                var result = await subStep.ExecuteAsync(context);

                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Sub-step {SubStepName} failed: {Message}", subStep.Name, result.Message);
                    throw new FlowExecutionException($"Sub-step {subStep.Name} failed: {result.Message}");
                }

                _logger.LogDebug("Sub-step {SubStepName} completed successfully", subStep.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing sub-step {SubStepName}", subStep.Name);
                throw;
            }
        }

        private async Task ExecuteStaticBranches(FlowContext context, List<FlowBranch> branches, CancellationToken cancellationToken)
        {
            foreach (var branch in branches)
            {
                bool shouldExecute = branch.IsDefault || (branch.Condition?.Invoke(context) == true);

                if (shouldExecute)
                {
                    foreach (var subStep in branch.SubSteps)
                    {
                        await ExecuteSubStep(context, subStep, cancellationToken);
                    }

                    break; // Only execute first matching branch
                }
            }
        }

        private async Task TriggerFlow(FlowContext context, Type flowType)
        {
            // Implementation to trigger another flow
            _logger.LogInformation("Triggering flow {FlowType} from step {StepName}", flowType.Name, context.CurrentStep.Name);

            // Get the flow engine service to trigger the flow
            var flowEngineService = context.Services.GetRequiredService<IFlowEngineService>();

            // Use reflection to call the generic TriggerAsync method
            var method = typeof(IFlowEngineService).GetMethod(nameof(IFlowEngineService.TriggerAsync));
            var genericMethod = method.MakeGenericMethod(flowType);

            var task = (Task)genericMethod.Invoke(flowEngineService, new object[] { context, null });
            await task;
        }
    }
}
