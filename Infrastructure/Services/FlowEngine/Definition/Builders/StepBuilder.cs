using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Definition.Builders
{
    /// <summary>
    /// Fluent builder for flow steps
    /// </summary>
    public class StepBuilder
    {
        private readonly FlowStep _step;
        private List<FlowStep> _steps { get; }

        internal StepBuilder(string name, List<FlowStep> steps)
        {
            _step = new FlowStep { Name = name };
            _steps = steps;
        }

        public StepBuilder RequiresData<TData>(string key)
        {
            _step.DataDependencies.Add(key, typeof(TData));
            return this;
        }

        /// <summary>
        /// Define the step execution logic
        /// </summary>
        public StepBuilder Execute(Func<FlowContext, Task<StepResult>> execution)
        {
            _step.ExecuteAsync = execution;
            return this;
        }

        /// <summary>
        /// Add conditional logic
        /// </summary>
        public StepBuilder OnlyIf(Func<FlowContext, bool> condition)
        {
            _step.Condition = condition;
            return this;
        }

        /// <summary>
        /// Add dependencies
        /// </summary>
        public StepBuilder After(params string[] stepNames)
        {
            _step.StepDependencies.AddRange(stepNames);
            return this;
        }

        /// <summary>
        /// Enable parallel execution
        /// </summary>
        public StepBuilder InParallel()
        {
            _step.CanRunInParallel = true;
            return this;
        }

        /// <summary>
        /// Configure retries
        /// </summary>
        public StepBuilder WithRetries(int maxRetries, TimeSpan? delay = null)
        {
            _step.MaxRetries = maxRetries;
            _step.RetryDelay = delay ?? TimeSpan.FromSeconds(1);
            return this;
        }

        /// <summary>
        /// Set timeout
        /// </summary>
        public  StepBuilder WithTimeout(TimeSpan timeout)
        {
            _step.Timeout = timeout;
            return this;
        }

        /// <summary>
        /// Add custom middleware to this step
        /// </summary>
        public StepBuilder UseMiddleware<TMiddleware>() where TMiddleware : IFlowMiddleware
        {
            _step.Middleware.Add(typeof(TMiddleware));
            return this;
        }

        /// <summary>
        /// Enable dynamic sub-branching based on runtime data
        /// </summary>
        public StepBuilder WithDynamicBranches<TItem>(
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
        public StepBuilder WithBranches(Action<FlowBranchBuilder> configureBranches)
        {
            var branchBuilder = new FlowBranchBuilder(_step);
            configureBranches(branchBuilder);
            return this;
        }

        public StepBuilder JumpTo(string stepName)
        {
            _step.JumpTo = stepName;
            return this;
        }

        /// <summary>
        /// Trigger another flow upon completion
        /// </summary>
        public StepBuilder Triggers<TFlow>() where TFlow : FlowDefinition
        {
            _step.TriggeredFlows.Add(typeof(TFlow));
            return this;
        }

        /// <summary>
        /// Mark as critical step (immediate persistence)
        /// </summary>
        public StepBuilder Critical()
        {
            _step.IsCritical = true;
            return this;
        }

        /// <summary>
        /// Marks the current step as idempotent, ensuring that it can be executed multiple times without changing the
        /// result beyond the initial application.
        /// </summary>
        /// <remarks>Idempotency is useful in scenarios where the step may be retried or executed multiple
        /// times,  such as in distributed systems or fault-tolerant workflows.</remarks>
        /// <returns>The current <see cref="FlowStepBuilder"/> instance, allowing for method chaining.</returns>
        public StepBuilder WithIdempotency()
        {
            _step.IsIdempotent = true;
            return this;
        }

        /// <summary>
        /// Allows the flow to continue even if this step fails
        /// </summary>
        /// <returns></returns>
        public StepBuilder AllowFailure()
        {
            _step.AllowFailure = true;
            return this;
        }

        /// <summary>
        /// Configure pause behavior for this step
        /// </summary>
        public StepBuilder CanPause(Func<FlowContext, PauseCondition> pauseCondition)
        {
            _step.PauseCondition = pauseCondition;
            return this;
        }

        /// <summary>
        /// Configure how this step can be resumed
        /// </summary>
        public StepBuilder ResumeOn(Action<ResumeConfigBuilder> configureResume)
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
        public void Build()
        {
            _steps.Add(_step);
        }
    }
}
