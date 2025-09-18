using Infrastructure.Services.FlowEngine.Core.Builders;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public abstract class FlowDefinition
    {
        private bool _isInitialized = false;

        protected readonly FlowStepBuilder _builder;

        /// <summary>
        /// Gets the list of middleware types to be executed in the application pipeline.
        /// </summary>
        public List<Type> Middleware { get; protected set; } = [];

        /// <summary>
        /// Gets the collection of steps that define the flow.
        /// </summary>
        public List<FlowStep> Steps { get; protected set; } = [];

        /// <summary>
        /// Gets or sets the active resume configuration.
        /// </summary>
        public ResumeConfig ActiveResumeConfig { get; set; }

        protected FlowDefinition()
        {
            _builder = new FlowStepBuilder(this);
        }

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
        public void Initialize()
        {
            if (_isInitialized) return;

            ConfigureMiddleware();
            DefineSteps();
            _isInitialized = true;
        }
    }
}
