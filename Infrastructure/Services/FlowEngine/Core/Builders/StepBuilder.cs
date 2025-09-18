using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Core.Builders
{
    /// <summary>
    /// Fluent builder for flow steps
    /// </summary>
    public class StepBuilder<TStep> where TStep : FlowStep, new()
    {
        private readonly TStep _step;
        private List<TStep> _steps { get; }

        internal StepBuilder(string name, List<TStep> steps)
        {
            _step = new TStep { Name = name };
            _steps = steps;
        }

        public StepBuilder<TStep> RequiresData<TData>(string key)
        {
            _step.DataDependencies.Add(key, typeof(TData));
            return this;
        }

        public StepBuilder<TStep> RequiresRole( params string[] roles)
        {
            _step.RequiredRoles.AddRange(roles);
            return this;
        }

        /// <summary>
        /// Define the step execution logic
        /// </summary>
        public StepBuilder<TStep> Execute(Func<FlowExecutionContext, Task<StepResult>> execution)
        {
            _step.ExecuteAsync = execution;
            return this;
        }

        /// <summary>
        /// Add conditional logic
        /// </summary>
        public StepBuilder<TStep> OnlyIf(Func<FlowExecutionContext, bool> condition)
        {
            _step.Condition = condition;
            return this;
        }

        /// <summary>
        /// Add dependencies
        /// </summary>
        public StepBuilder<TStep> After(params string[] stepNames)
        {
            _step.StepDependencies.AddRange(stepNames);
            return this;
        }

        /// <summary>
        /// Enable parallel execution
        /// </summary>
        public StepBuilder<TStep> InParallel()
        {
            _step.CanRunInParallel = true;
            return this;
        }

        /// <summary>
        /// Configure retries
        /// </summary>
        public StepBuilder<TStep> WithRetries(int maxRetries, TimeSpan? delay = null)
        {
            _step.MaxRetries = maxRetries;
            _step.RetryDelay = delay ?? TimeSpan.FromSeconds(1);
            return this;
        }

        /// <summary>
        /// Set timeout
        /// </summary>
        public  StepBuilder<TStep> WithTimeout(TimeSpan timeout)
        {
            _step.Timeout = timeout;
            return this;
        }

        /// <summary>
        /// Add custom middleware to this step
        /// </summary>
        public StepBuilder<TStep> UseMiddleware<TMiddleware>() where TMiddleware : IFlowMiddleware
        {
            _step.Middleware.Add(typeof(TMiddleware));
            return this;
        }

        /// <summary>
        /// Enable dynamic sub-branching based on runtime data
        /// </summary>
        public StepBuilder<TStep> WithDynamicBranches<TItem>(
            Func<FlowExecutionContext, IEnumerable<TItem>> dataSelector,
            Func<FlowBranchBuilder, TItem, int, FlowBranch> stepFactory,
            ExecutionStrategy strategy = ExecutionStrategy.Parallel)
        {
            _step.DynamicBranching = new DynamicBranchingConfig
            {
                DataSelector = ctx => dataSelector(ctx).Cast<object>(),
                BranchFactory = ((builder, item, index) => stepFactory(builder, (TItem)item, index)),
                ExecutionStrategy = strategy
            };
            return this;
        }

        /// <summary>
        /// Enable static conditional sub-branching
        /// </summary>
        public StepBuilder<TStep> WithStaticBranches(Action<FlowBranchBuilder> stepFactory)
        {
            var branchBuilder = new FlowBranchBuilder(_step);
            stepFactory(branchBuilder);
            return this;
        }

        public StepBuilder<TStep> JumpTo(string stepName, int? maxJumps = null)
        {
            _step.JumpTo = stepName;
            _step.MaxJumps = maxJumps ?? 10; // Default to 10 if not specified to prevent infinite loops
            return this;
        }

        /// <summary>
        /// Trigger another flow upon completion
        /// </summary>
        public StepBuilder<TStep> Triggers<TFlow>(Func<FlowExecutionContext, Dictionary<string, object>>? initialDataFactory = null) where TFlow : FlowDefinition
        {
            _step.TriggeredFlows.Add(new TriggeredFlowData(typeof(TFlow).AssemblyQualifiedName, initialDataFactory));
            return this;
        }

        /// <summary>
        /// Mark as critical step (immediate persistence)
        /// </summary>
        public StepBuilder<TStep> Critical()
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
        public StepBuilder<TStep> WithIdempotency(string? key = null)
        {
            _step.IsIdempotent = true;
            _step.IdempotencyKey = key;
            return this;
        }

        public StepBuilder<TStep> WithIdempotency(Func<FlowExecutionContext, string> keyFactory)
        {
            _step.IsIdempotent = true;
            _step.IdempotencyKeyFactory = keyFactory;
            return this;
        }

        /// <summary>
        /// Allows the flow to continue even if this step fails
        /// </summary>
        /// <returns></returns>
        public StepBuilder<TStep> AllowFailure()
        {
            _step.AllowFailure = true;
            return this;
        }

        /// <summary>
        /// Configure pause behavior for this step
        /// </summary>
        public StepBuilder<TStep> CanPause(Func<FlowExecutionContext, Task<PauseCondition>> pauseCondition)
        {
            _step.PauseCondition = pauseCondition;
            return this;
        }

        /// <summary>
        /// Configure how this step can be resumed
        /// </summary>
        public StepBuilder<TStep> ResumeOn(Action<ResumeConfigBuilder> configureResume)
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
        public TStep Build()
        {
            _steps.Add(_step);
            return _step;
        }
    }
}
