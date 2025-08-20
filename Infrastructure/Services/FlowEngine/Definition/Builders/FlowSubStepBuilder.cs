using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Definition.Builders
{
    /// <summary>
    /// Builder for sub-steps within branches
    /// </summary>
    public class FlowSubStepBuilder
    {
        private readonly FlowBranch _branch;

        internal FlowSubStepBuilder(FlowBranch branch)
        {
            _branch = branch;
        }

        /// <summary>
        /// Add a sub-step
        /// </summary>
        public FlowSubStepBuilder Step(string name, Func<FlowContext, Task<StepResult>> execution)
        {
            _branch.SubSteps.Add(new FlowSubStep
            {
                Name = name,
                ExecuteAsync = execution
            });
            return this;
        }
    }
}
