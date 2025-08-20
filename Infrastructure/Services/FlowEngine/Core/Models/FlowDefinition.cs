using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Services.FlowEngine.Definition.Builders;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Base class for defining flows with declarative syntax
    /// </summary>
    public abstract class FlowDefinition
    {
        public string FlowId { get; set; }
        public string UserId { get; set; }
        public string CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public FlowStatus Status { get; set; }
        public string CurrentStepName { get; set; }
        public int CurrentStepIndex { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
        public List<FlowStep> Steps { get; protected set; } = new();
        public List<FlowEvent> Events { get; set; } = new();
        public Exception LastError { get; set; }

        // NEW: Pause/Resume state
        public DateTime? PausedAt { get; set; }
        public PauseReason? PauseReason { get; set; }
        public string PauseMessage { get; set; }
        public ResumeConfig ActiveResumeConfig { get; set; }
        public Dictionary<string, object> PauseData { get; set; } = new();

        /// <summary>
        /// Define your flow steps - implement this in your flow class
        /// </summary>
        protected abstract void DefineSteps();

        /// <summary>
        /// Initialize the flow (called automatically)
        /// </summary>
        public virtual void Initialize()
        {
            DefineSteps();
            Status = FlowStatus.Ready;
        }

        /// <summary>
        /// Set initial data for the flow
        /// </summary>
        public void SetData(object data)
        {
            if (data is Dictionary<string, object> dict)
            {
                Data = dict;
            }
            else
            {
                // Convert object to dictionary
                var properties = data.GetType().GetProperties();
                foreach (var prop in properties)
                {
                    Data[prop.Name] = prop.GetValue(data);
                }
            }
        }

        /// <summary>
        /// Get typed data from flow
        /// </summary>
        public T GetData<T>(string key)
        {
            return Data.ContainsKey(key) ? (T)Data[key] : default(T);
        }

        /// <summary>
        /// Fluent API for defining steps
        /// </summary>
        protected FlowStepBuilder Step(string name)
        {
            return new FlowStepBuilder(name, this);
        }
    }
}
