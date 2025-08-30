using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Definition.Builders
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
