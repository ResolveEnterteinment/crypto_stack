using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Engine;

namespace Infrastructure.Services.FlowEngine.Core.Builders
{
    /// <summary>
    /// Fluent builder for flow steps
    /// </summary>
    public class FlowStepBuilder
    {
        private readonly FlowDefinition _flow;

        public FlowStepBuilder(FlowDefinition flow)
        {
            _flow = flow;
        }

        /// <summary>
        /// Fluent API for defining steps
        /// </summary>
        public StepBuilder<FlowStep> Step(string name)
        {
            return new StepBuilder<FlowStep>(name, _flow.Steps);
        }
    }
}
