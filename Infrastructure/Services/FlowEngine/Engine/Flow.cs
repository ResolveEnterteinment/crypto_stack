using Infrastructure.Services.FlowEngine.Core.Builders;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;
using Microsoft.Extensions.DependencyInjection;

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
        private CancellationToken? _externalCancellationToken;

        // Service scope management
        private IServiceScope? _currentScope;
        private readonly object _serviceLock = new object();

        public Guid Id => State.FlowId;
        public FlowStatus Status => State.Status;

        public CancellationToken CancellationToken => _externalCancellationToken.HasValue ? _externalCancellationToken.Value : _cancellationTokenSource?.Token ?? CancellationToken.None;

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

        private Flow(IServiceScopeFactory serviceScopeFactory, CancellationToken? cancellationToken = null)
        {
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _externalCancellationToken = cancellationToken;
        }

        /// <summary>
        /// Gets a service with automatic scope management and recreation
        /// </summary>
        /// <typeparam name="TService">The service type to retrieve</typeparam>
        /// <returns>The requested service instance</returns>
        public TService GetService<TService>() where TService : class
        {
            lock (_serviceLock)
            {
                EnsureValidScope();
                return _currentScope!.ServiceProvider.GetRequiredService<TService>();
            }
        }

        /// <summary>
        /// Ensures we have a valid service scope, recreating if necessary
        /// </summary>
        private void EnsureValidScope()
        {
            if (_currentScope == null || IsScopeConsumed())
            {
                RecreateScope();
            }
        }

        /// <summary>
        /// Checks if the current service scope has been consumed/disposed
        /// </summary>
        private bool IsScopeConsumed()
        {
            if (_currentScope == null)
                return true;

            try
            {
                // Try to access the service provider - if disposed, this will throw
                _ = _currentScope.ServiceProvider.GetService<IServiceScopeFactory>();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        /// <summary>
        /// Recreates the service scope
        /// </summary>
        private void RecreateScope()
        {
            _currentScope?.Dispose();
            _currentScope = _serviceScopeFactory.CreateScope();
        }

        /// <summary>
        /// Creates a fresh service scope for service resolution
        /// </summary>
        private IServiceScope CreateServiceScope()
        {
            return _serviceScopeFactory.CreateScope();
        }

        /// <summary>
        /// Create a new flow instance from definition
        /// </summary>
        public static Flow Create<TDefinition>(
            IServiceProvider serviceProvider, 
            Dictionary<string, object>? initialData = null,
            CancellationToken? cancellationToken = null
            ) where TDefinition : FlowDefinition
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var flow = new Flow(scopeFactory, cancellationToken);

            // Create fresh definition instance from DI using a new scope
            using var scope = scopeFactory.CreateScope();
            flow.Definition = scope.ServiceProvider.GetRequiredService<TDefinition>();

            // Create new state
            flow.State = new FlowState
            {
                FlowId = Guid.NewGuid(),
                FlowType = flow.Definition.GetType().AssemblyQualifiedName,
                Status = FlowStatus.Ready,
                CreatedAt = DateTime.UtcNow,
                Data = initialData?.ToSafe() ?? new Dictionary<string, SafeObject>(),
                Version = 1
            };

            flow._cancellationTokenSource = new CancellationTokenSource();

            flow._externalCancellationToken = cancellationToken;

            flow.Initialize();

            // Don't assign disposed service provider - will be refreshed when needed
            return flow;
        }

        /// <summary>
        /// Create a new flow instance from definition
        /// </summary>
        public static Flow Create(
            IServiceProvider serviceProvider, 
            Type definitionType, 
            Dictionary<string, object>? initialData = null,
            CancellationToken? cancellationToken = null)
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var flow = new Flow(scopeFactory, cancellationToken);

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
                FlowType = definitionType.AssemblyQualifiedName!,
                Status = FlowStatus.Ready,
                CreatedAt = DateTime.UtcNow,
                Data = initialData?.ToSafe() ?? [],
                Version = 1
            };

            flow._cancellationTokenSource = new CancellationTokenSource();

            flow._externalCancellationToken = cancellationToken;

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

            // Create flow with no initial cancellation token - will be set up later during restoration
            var flow = new Flow(scopeFactory, CancellationToken.None);

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
                        CurrentStep = pauseStep,
                        Services = contextScope.ServiceProvider,
                    };

                    var pauseCondition = await pauseStep.PauseCondition(tempContext);
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
        public static FlowBuilder Builder(IServiceProvider serviceProvider, CancellationToken? cancellationToken = null)
        {
            return new FlowBuilder(serviceProvider, cancellationToken);
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
                CurrentStep = Definition.Steps[State.CurrentStepIndex],
                Services = null // Will be set when needed via RefreshServiceContext
            };

            IsInitialized = true;
        }

        /// <summary>
        /// Execute the flow with a fresh service scope
        /// </summary>
        public async Task<FlowExecutionResult> ExecuteAsync()
        {
            await _executionLock.WaitAsync(CancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                var executor = GetService<IFlowExecutor>();

                // Update the context with the current service provider
                if (Context != null)
                {
                    lock (_serviceLock)
                    {
                        EnsureValidScope();
                        Context.Services = _currentScope!.ServiceProvider;
                    }
                }

                return await executor.ExecuteAsync(this, CancellationToken);
            }
            finally
            {
                _executionLock.Release();
            }
        }

        public async Task<FlowExecutionResult> ResumeAsync(string reason)
        {
            await _executionLock.WaitAsync(CancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                var executor = GetService<IFlowExecutor>();

                // Update the context with the current service provider
                if (Context != null)
                {
                    lock (_serviceLock)
                    {
                        EnsureValidScope();
                        Context.Services = _currentScope!.ServiceProvider;
                    }
                }

                return await executor.ResumePausedFlowAsync(this, reason, CancellationToken);
            }
            finally
            {
                _executionLock.Release();
            }
        }

        public async Task<FlowExecutionResult> RetryAsync(string reason)
        {
            await _executionLock.WaitAsync(CancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                var executor = GetService<IFlowExecutor>();

                // Update the context with the current service provider
                if (Context != null)
                {
                    lock (_serviceLock)
                    {
                        EnsureValidScope();
                        Context.Services = _currentScope!.ServiceProvider;
                    }
                }

                return await executor.RetryFailedFlowAsync(this, reason, CancellationToken);
            }
            finally
            {
                _executionLock.Release();
            }
        }

        public async Task<FlowExecutionResult> PauseAsync(PauseCondition pauseCondition)
        {
            await _executionLock.WaitAsync(CancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                var executor = GetService<IFlowExecutor>();

                // Update the context with the current service provider
                if (Context != null)
                {
                    lock (_serviceLock)
                    {
                        EnsureValidScope();
                        Context.Services = _currentScope!.ServiceProvider;
                    }
                }

                var stepIndex = Context?.CurrentStep.Status == StepStatus.Pending ? State.CurrentStepIndex : State.CurrentStepIndex + 1;
                var pauseStep = Definition.Steps[stepIndex];

                // Fix: Ensure PauseCondition is an async function returning Task<PauseCondition>
                pauseStep.PauseCondition = async _ => await Task.FromResult(pauseCondition);

                return FlowExecutionResult.Paused(Id, "Flow paused successfully.");
            }
            finally
            {
                _executionLock.Release();
            }
        }

        public async Task<FlowExecutionResult> CancelAsync(string reason)
        {
            await _executionLock.WaitAsync(CancellationToken);
            try
            {
                // Create a fresh scope for execution to ensure we have valid services
                var executor = GetService<IFlowExecutor>();

                // Update the context with the current service provider
                if (Context != null)
                {
                    lock (_serviceLock)
                    {
                        EnsureValidScope();
                        Context.Services = _currentScope!.ServiceProvider;
                    }
                }

                return executor.CancelFlowAsync(this, reason, CancellationToken).Result;
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

            // Only persist if the state is dirty
            if (!State.IsDirty)
            {
                return; // No changes to persist
            }

            using var scope = CreateServiceScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IFlowPersistence>();
            await persistence.SaveFlowStateAsync(State);

            // Mark State as clean after successful persistence
            State.MarkClean();
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

            // Dispose the current scope
            lock (_serviceLock)
            {
                _currentScope?.Dispose();
                _currentScope = null;
            }
        }

        public void LinkToParentCancellation(CancellationToken parentToken)
        {
            // Dispose existing token source if any
            _cancellationTokenSource?.Dispose();

            // Create a new linked token source
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken);

            // Update external token reference
            _externalCancellationToken = _cancellationTokenSource.Token;
        }

        // Update the existing InitializeCancellationTokenSource method
        internal void InitializeCancellationTokenSource(CancellationToken cancellationToken)
        {
            // Dispose existing token source if any
            _cancellationTokenSource?.Dispose();

            if (cancellationToken == CancellationToken.None)
            {
                // Create independent cancellation token source
                _cancellationTokenSource = new CancellationTokenSource();
                _externalCancellationToken = _cancellationTokenSource.Token;
            }
            else
            {
                // Link to provided cancellation token
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _externalCancellationToken = _cancellationTokenSource.Token;
            }
        }
    }
}