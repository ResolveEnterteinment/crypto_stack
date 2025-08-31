using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Base class for defining flows with declarative syntax
    /// </summary>
    public abstract class FlowDefinition
    {
        public bool IsInitialized { get; private set; } = false;
        public Guid FlowId { get; set; }
        public string UserId { get; set; }
        public string CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public FlowStatus Status { get; set; }
        public string CurrentStepName { get; set; }
        public int CurrentStepIndex { get; set; } = 0;
        public Dictionary<string, SafeObject> Data { get; set; } = new();
        public List<FlowStep> Steps { get; protected set; } = new();
        public List<FlowEvent> Events { get; set; } = new();
        public Exception LastError { get; set; }

        // NEW: Pause/Resume state
        public DateTime? PausedAt { get; set; }
        public PauseReason? PauseReason { get; set; }
        public string? PauseMessage { get; set; }
        public ResumeConfig ActiveResumeConfig { get; set; }
        public Dictionary<string, SafeObject> PauseData { get; set; } = new();

        // NEW: Flow-level middleware
        public List<Type> Middleware { get; protected set; } = new();

        /// <summary>
        /// Define your flow steps - implement this in your flow class
        /// </summary>
        protected abstract void DefineSteps();

        /// <summary>
        /// Configure flow-level middleware that applies to all steps
        /// </summary>
        protected virtual void ConfigureMiddleware()
        {
            // Override in derived classes to add flow-specific middleware
        }

        /// <summary>
        /// Add middleware to this flow
        /// </summary>
        protected void UseMiddleware<TMiddleware>() where TMiddleware : class, IFlowMiddleware
        {
            Middleware.Add(typeof(TMiddleware));
        }

        /// <summary>
        /// Initialize the flow (called automatically)
        /// </summary>
        public virtual void Initialize()
        {
            if(IsInitialized) return;

            ConfigureMiddleware();
            DefineSteps();
            Status = FlowStatus.Ready;
            IsInitialized = true;
        }

        /// <summary>
        /// Set initial data for the flow
        /// </summary>
        public void SetInitialData(Dictionary<string, object> data)
        {
            Data = data.ToSafe();
        }

        /// <summary>
        /// Get typed data from flow
        /// </summary>
        public T GetData<T>(string key)
        {
            return Data.TryGetValue(key, out var safeObj) ? safeObj.ToValue<T>() : default(T);
        }

        /// <summary>
        /// Set data in the flow's data dictionary
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>

        public void SetData(string key, object value)
        {
            Data[key] = SafeObject.FromValue(value);
        }
        public static FlowDefinition FromDocument<TFlow>(FlowDocument document) where TFlow : FlowDefinition, new()
        {
            var flow = new TFlow
            {
                FlowId = document.FlowId,
                UserId = document.UserId,
                CorrelationId = document.CorrelationId,
                CreatedAt = document.CreatedAt,
                StartedAt = document.StartedAt,
                CompletedAt = document.CompletedAt,
                Status = document.Status,
                CurrentStepName = document.CurrentStepName,
                CurrentStepIndex = document.CurrentStepIndex,
                Data = document.Data ?? [],
                Events = document.Events ?? new List<FlowEvent>(),
                LastError = document.LastError,

                // Copy pause/resume state
                PausedAt = document.PausedAt,
                PauseReason = document.PauseReason,
                PauseMessage = document.PauseMessage,
                PauseData = document.PauseData ?? []
            };

            // Fix for CS0272: Use the collection initializer to populate the Steps property
            if (document.Steps != null)
            {
                foreach (var step in document.Steps)
                {
                    var tagetStep = flow.Steps.Find(s => s.Name == step.Name);
                    if (tagetStep == null) throw new FlowExecutionException("Failed to copy step state. Flow step not found.");

                    tagetStep.Status = step.Status;
                    if (step.Result != null) tagetStep.Result = step.Result;

                    if (step.Branches != null && step.Branches.Count > 0)
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

            return flow;
        }
    }
}