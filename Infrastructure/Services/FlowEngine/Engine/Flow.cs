using Infrastructure.Services.Base;
using Infrastructure.Services.FlowEngine.Core.Builders;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Infrastructure.Services.FlowEngine.Engine
{
    /// <summary>
    /// Main Flow class that combines definition and state with execution methods
    /// </summary>
    public class Flow
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly SemaphoreSlim _executionLock = new(1, 1);
        private CancellationTokenSource? _cancellationTokenSource;

        public Guid Id => State.FlowId;
        public FlowStatus Status => State.Status;

        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        /// <summary>
        /// Flow definition containing steps, middleware, and configuration
        /// </summary>
        public FlowDefinition Definition { get; private set; }

        /// <summary>
        /// Serializable flow state for persistence
        /// </summary>
        public FlowState State { get; private set; }

        /// <summary>
        /// Runtime execution context
        /// </summary>
        public FlowExecutionContext Context { get; private set; }

        public bool IsInitialized { get; private set; } = false;

        private Flow(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        }

        /// <summary>
        /// Creates a fresh service scope for service resolution
        /// </summary>
        private IServiceScope CreateServiceScope()
        {
            return _serviceScopeFactory.CreateScope();
        }

        /// <summary>
        /// Safely gets a required service using a fresh scope
        /// </summary>
        private T GetRequiredServiceSafe<T>() where T : class
        {
            using var scope = CreateServiceScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// Safely gets an optional service using a fresh scope
        /// </summary>
        private T GetServiceSafe<T>() where T : class
        {
            using var scope = CreateServiceScope();
            return scope.ServiceProvider.GetService<T>();
        }

        /// <summary>
        /// Create a new flow instance from definition
        /// </summary>
        public static Flow Create<TDefinition>(IServiceProvider serviceProvider, Dictionary<string, object>? initialData = null) where TDefinition : FlowDefinition
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var flow = new Flow(scopeFactory);

            // Create fresh definition instance from DI using a new scope
            using var scope = scopeFactory.CreateScope();
            flow.Definition = scope.ServiceProvider.GetRequiredService<TDefinition>();

            // Create new state
            flow.State = new FlowState
            {
                FlowId = Guid.NewGuid(),
                FlowType = typeof(TDefinition).FullName!,
                Status = FlowStatus.Ready,
                CreatedAt = DateTime.UtcNow,
                Data = initialData?.ToSafe() ?? new Dictionary<string, SafeObject>(),
                Version = 1
            };

            flow._cancellationTokenSource = new CancellationTokenSource();

            flow.Initialize();

            // Don't assign disposed service provider - will be refreshed when needed
            return flow;
        }

        /// <summary>
        /// Create a new flow instance from definition
        /// </summary>
        public static Flow Create(IServiceProvider serviceProvider, Type definitionType, Dictionary<string, object>? initialData = null)
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var flow = new Flow(scopeFactory);

            // Create fresh definition instance from DI using a new scope
            using var scope = scopeFactory.CreateScope();

            // Create an instance of Type FlowState.FlowType using DI
            if (definitionType == null || !typeof(FlowDefinition).IsAssignableFrom(definitionType))
                throw new InvalidOperationException($"Invalid flow definition type: {definitionType.Name}");

            // Try to get from DI container first (recommended) using a fresh scope
            FlowDefinition flowDefinition = null;

            try
            {
                flowDefinition = (FlowDefinition)scope.ServiceProvider.GetRequiredService(definitionType);
            }
            catch (InvalidOperationException)
            {
                flowDefinition = (FlowDefinition)Activator.CreateInstance(definitionType);
            }

            if (flowDefinition == null)
                throw new InvalidOperationException($"Could not create instance of flow type: {definitionType.Name}");

            flow.Definition = flowDefinition;

            // Create new state
            flow.State = new FlowState
            {
                FlowId = Guid.NewGuid(),
                FlowType = definitionType.FullName!,
                Status = FlowStatus.Ready,
                CreatedAt = DateTime.UtcNow,
                Data = initialData?.ToSafe() ?? [],
                Version = 1
            };

            flow._cancellationTokenSource = new CancellationTokenSource();

            flow.Initialize();

            // Don't assign disposed service provider - will be refreshed when needed
            return flow;
        }


        /// <summary>
        /// Restore flow from persisted state
        /// </summary>
        public static async Task<Flow> FromStateAsync(FlowState flowState, IServiceProvider serviceProvider)
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            // Create an instance of Type flowDoc.FlowType using DI
            var flowType = Type.GetType(flowState.FlowType);

            if (flowType == null || !typeof(FlowDefinition).IsAssignableFrom(flowType))
                throw new InvalidOperationException($"Invalid flow type: {flowState.FlowType}");

            // Try to get from DI container first (recommended) using a fresh scope
            FlowDefinition flowDefinition = null;
            using (var scope = scopeFactory.CreateScope())
            {
                try
                {
                    flowDefinition = (FlowDefinition)scope.ServiceProvider.GetRequiredService(flowType);
                }
                catch (InvalidOperationException)
                {
                    flowDefinition = (FlowDefinition)Activator.CreateInstance(flowType);
                }
            }

            if (flowDefinition == null)
                throw new InvalidOperationException($"Could not create instance of flow type: {flowState.FlowType}");

            var flow = new Flow(scopeFactory);

            flow.Definition = flowDefinition;
            flow.State = flowState;

            flow.Initialize();

            if (flow.State.Status == FlowStatus.Paused)
            {
                var pauseStep = flow.Definition.Steps[flow.State.CurrentStepIndex];

                // Use fresh scope for pause condition evaluation if needed
                if (pauseStep.PauseCondition != null)
                {
                    using var contextScope = scopeFactory.CreateScope();
                    // Create temporary context for pause condition evaluation
                    var tempContext = new FlowExecutionContext
                    {
                        Flow = flow,
                        State = flow.State,
                        Definition = flow.Definition,
                        CurrentStep = pauseStep,
                        Services = contextScope.ServiceProvider,
                        CancellationToken = flow.CancellationToken
                    };

                    var pauseCondition = pauseStep.PauseCondition(tempContext);
                    flow.Definition.ActiveResumeConfig = pauseCondition.ResumeConfig ?? pauseStep.ResumeConfig;
                }
            }

            return flow;
        }

        /// <summary>
        /// Creates a fluent builder for constructing and configuring flows
        /// </summary>
        /// <typeparam name="TDefinition">The flow definition type</typeparam>
        /// <param name="serviceProvider">Service provider for dependency injection</param>
        /// <returns>A FlowBuilder instance for chainable configuration</returns>
        public static FlowBuilder Builder(IServiceProvider serviceProvider)
        {
            return new FlowBuilder(serviceProvider);
        }

        /// <summary>
        /// Initialize the flow (called automatically)
        /// </summary>
        public virtual void Initialize()
        {
            if (IsInitialized) return;

            Definition.Initialize();

            if (Definition == null)
                throw new InvalidOperationException("Flow definition must be set before initialization.");

            if (State.Steps.Count > 0)
            { // Restore step states if state exists
                RestoreStepStatesAsync();
            }
            else
            { // New flow, initialize step states

                State.Steps = Definition.Steps.Select(s => new StepState(s)).ToList();
                State.Status = FlowStatus.Ready;
            }

            Context = new FlowExecutionContext
            {
                Flow = this,
                State = State,
                Definition = Definition,
                CurrentStep = Definition.Steps[State.CurrentStepIndex],
                CancellationToken = CancellationToken,
                Services = null // Will be set when needed via RefreshServiceContext
            };

            IsInitialized = true;
        }

        /// <summary>
        /// Set initial data for the flow
        /// </summary>
        public void SetInitialData(Dictionary<string, object> data)
        {
            State.Data = data.ToSafe();
        }

        /// <summary>
        /// Execute the flow with a fresh service scope
        /// </summary>
        public async Task<FlowExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await _executionLock.WaitAsync(cancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                using var scope = CreateServiceScope();
                var executor = scope.ServiceProvider.GetRequiredService<IFlowExecutor>();

                // Update the context with the fresh service provider for this execution
                if (Context != null)
                {
                    Context.Services = scope.ServiceProvider;
                }

                return await executor.ExecuteAsync(this, cancellationToken);
            }
            finally
            {
                _executionLock.Release();
            }
        }

        public async Task<FlowExecutionResult> ResumeAsync(string reason, CancellationToken cancellationToken = default)
        {
            await _executionLock.WaitAsync(cancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                using var scope = CreateServiceScope();
                var executor = scope.ServiceProvider.GetRequiredService<IFlowExecutor>();
                // Update the context with the fresh service provider for this execution
                if (Context != null)
                {
                    Context.Services = scope.ServiceProvider;
                }
                return await executor.ResumePausedFlowAsync(this, reason, cancellationToken);
            }
            finally
            {
                _executionLock.Release();
            }
        }

        public async Task<FlowExecutionResult> RetryAsync(string reason, CancellationToken cancellationToken = default)
        {
            await _executionLock.WaitAsync(cancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                using var scope = CreateServiceScope();
                var executor = scope.ServiceProvider.GetRequiredService<IFlowExecutor>();
                // Update the context with the fresh service provider for this execution
                if (Context != null)
                {
                    Context.Services = scope.ServiceProvider;
                }
                return await executor.RetryFailedFlowAsync(this, reason, cancellationToken);
            }
            finally
            {
                _executionLock.Release();
            }
        }

        public async Task<FlowExecutionResult> PauseAsync(PauseCondition pauseCondition, CancellationToken cancellationToken = default)
        {
            await _executionLock.WaitAsync(cancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                using var scope = CreateServiceScope();
                var executor = scope.ServiceProvider.GetRequiredService<IFlowExecutor>();
                // Update the context with the fresh service provider for this execution
                if (Context != null)
                {
                    Context.Services = scope.ServiceProvider;
                }
                var stepIndex = Context?.CurrentStep.Status == StepStatus.Pending ? State.CurrentStepIndex : State.CurrentStepIndex + 1;
                var pasuseStep = Definition.Steps[stepIndex];

                pasuseStep.PauseCondition = _ => pauseCondition;

                return FlowExecutionResult.Paused(Id, "Flow paused successfully.");
            }
            finally
            {
                _executionLock.Release();
            }
        }

        /// <summary>
        /// Persist flow state to storage using a fresh service scope
        /// </summary>
        public async Task PersistAsync()
        {
            State.Steps = Definition.Steps.Select(s => new StepState(s)).ToList();

            using var scope = CreateServiceScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IFlowPersistence>();
            await persistence.SaveFlowStateAsync(State);
        }

        /// <summary>
        /// Updates the execution context with a fresh service provider
        /// This is useful when the flow needs to be used after being restored or when services might have changed
        /// </summary>
        public void RefreshServiceContext()
        {
            if (Context != null)
            {
                using var scope = CreateServiceScope();
                Context.Services = scope.ServiceProvider;
            }
        }

        /// <summary>
        /// Executes an action with a fresh service scope
        /// </summary>
        public async Task<T> WithFreshServices<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = CreateServiceScope();
            return await action(scope.ServiceProvider);
        }

        /// <summary>
        /// Executes an action with a fresh service scope (void return)
        /// </summary>
        public async Task WithFreshServices(Func<IServiceProvider, Task> action)
        {
            using var scope = CreateServiceScope();
            await action(scope.ServiceProvider);
        }

        private void RestoreStepStatesAsync()
        {
            // Copy step states from persisted state back to definition
            if (State.Steps != null)
            {
                foreach (var stateStep in State.Steps)
                {
                    var definitionStep = Definition.Steps.FirstOrDefault(s => s.Name == stateStep.Name);

                    if (definitionStep == null) throw new InvalidOperationException($"Step '{stateStep.Name}' not found in flow definition.");

                    definitionStep.Status = stateStep.Status;
                    definitionStep.Branches = stateStep.Branches;
                    definitionStep.CurrentJumps = stateStep.CurrentJumps;
                    definitionStep.Result = stateStep.Result;
                }
            }
        }

        public void Dispose()
        {
            _executionLock?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}