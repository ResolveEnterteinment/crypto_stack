using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.Builders
{
    /// <summary>
    /// Builder for sub-steps within branches
    /// </summary>
    public class BranchStepBuilder
    {
        private readonly FlowBranch _branch;

        internal BranchStepBuilder(FlowBranch branch)
        {
            _branch = branch;
        }

        /// <summary>
        /// Fluent API for defining steps
        /// </summary>
        public StepBuilder<FlowSubStep> Step(string name)
        {
            return new StepBuilder<FlowSubStep>(name, _branch.Steps);
        }

    }
}
