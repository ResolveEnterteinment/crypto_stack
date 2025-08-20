using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Definition.Builders
{
    /// <summary>
    /// Fluent builder for flow steps
    /// </summary>
    public class FlowStepBuilder
    {
        private readonly FlowStep _step;
        private readonly FlowDefinition _flow;

        internal FlowStepBuilder(string name, FlowDefinition flow)
        {
            _step = new FlowStep { Name = name };
            _flow = flow;
        }

        /// <summary>
        /// Define the step execution logic
        /// </summary>
        public FlowStepBuilder Execute(Func<FlowContext, Task<StepResult>> execution)
        {
            _step.ExecuteAsync = execution;
            return this;
        }

        /// <summary>
        /// Add conditional logic
        /// </summary>
        public FlowStepBuilder OnlyIf(Func<FlowContext, bool> condition)
        {
            _step.Condition = condition;
            return this;
        }

        /// <summary>
        /// Add dependencies
        /// </summary>
        public FlowStepBuilder After(params string[] stepNames)
        {
            _step.Dependencies.AddRange(stepNames);
            return this;
        }

        /// <summary>
        /// Enable parallel execution
        /// </summary>
        public FlowStepBuilder InParallel()
        {
            _step.CanRunInParallel = true;
            return this;
        }

        /// <summary>
        /// Configure retries
        /// </summary>
        public FlowStepBuilder WithRetries(int maxRetries, TimeSpan? delay = null)
        {
            _step.MaxRetries = maxRetries;
            _step.RetryDelay = delay ?? TimeSpan.FromSeconds(1);
            return this;
        }

        /// <summary>
        /// Set timeout
        /// </summary>
        public FlowStepBuilder WithTimeout(TimeSpan timeout)
        {
            _step.Timeout = timeout;
            return this;
        }

        /// <summary>
        /// Add custom middleware to this step
        /// </summary>
        public FlowStepBuilder UseMiddleware<TMiddleware>() where TMiddleware : IFlowMiddleware
        {
            _step.Middleware.Add(typeof(TMiddleware));
            return this;
        }

        /// <summary>
        /// Enable dynamic sub-branching based on runtime data
        /// </summary>
        public FlowStepBuilder WithDynamicBranches<TItem>(
            Func<FlowContext, IEnumerable<TItem>> dataSelector,
            Func<TItem, int, FlowSubStep> stepFactory,
            ExecutionStrategy strategy = ExecutionStrategy.Parallel)
        {
            _step.DynamicBranching = new DynamicBranchingConfig
            {
                DataSelector = ctx => dataSelector(ctx).Cast<object>(),
                StepFactory = (item, index) => stepFactory((TItem)item, index),
                ExecutionStrategy = strategy
            };
            return this;
        }

        /// <summary>
        /// Enable static conditional sub-branching
        /// </summary>
        public FlowStepBuilder WithBranches(Action<FlowBranchBuilder> configureBranches)
        {
            var branchBuilder = new FlowBranchBuilder(_step);
            configureBranches(branchBuilder);
            return this;
        }

        /// <summary>
        /// Trigger another flow upon completion
        /// </summary>
        public FlowStepBuilder Triggers<TFlow>() where TFlow : FlowDefinition, new()
        {
            _step.TriggeredFlow = typeof(TFlow);
            return this;
        }

        /// <summary>
        /// Mark as critical step (immediate persistence)
        /// </summary>
        public FlowStepBuilder Critical()
        {
            _step.IsCritical = true;
            return this;
        }

        /// <summary>
        /// Configure pause behavior for this step
        /// </summary>
        public FlowStepBuilder CanPause(Func<FlowContext, PauseCondition> pauseCondition)
        {
            _step.PauseCondition = pauseCondition;
            return this;
        }

        /// <summary>
        /// Configure how this step can be resumed
        /// </summary>
        public FlowStepBuilder ResumeOn(Action<ResumeConfigBuilder> configureResume)
        {
            var resumeConfig = new ResumeConfig();
            var builder = new ResumeConfigBuilder(resumeConfig);
            configureResume(builder);
            _step.ResumeConfig = resumeConfig;
            return this;
        }

        /// <summary>
        /// Add step to flow and return flow for chaining
        /// </summary>
        public FlowDefinition Build()
        {
            _flow.Steps.Add(_step);
            return _flow;
        }
    }
}
