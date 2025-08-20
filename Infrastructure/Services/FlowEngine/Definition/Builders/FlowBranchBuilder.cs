using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Definition.Builders
{
    /// <summary>
    /// Builder for sub-branching logic
    /// </summary>
    public class FlowBranchBuilder
    {
        private readonly FlowStep _step;

        internal FlowBranchBuilder(FlowStep step)
        {
            _step = step;
        }

        /// <summary>
        /// Add a conditional branch
        /// </summary>
        public FlowBranchBuilder When(Func<FlowContext, bool> condition, Action<FlowSubStepBuilder> defineSteps)
        {
            var branch = new FlowBranch { Condition = condition };
            var builder = new FlowSubStepBuilder(branch);
            defineSteps(builder);
            _step.Branches.Add(branch);
            return this;
        }

        /// <summary>
        /// Add default branch (executed if no conditions match)
        /// </summary>
        public FlowBranchBuilder Otherwise(Action<FlowSubStepBuilder> defineSteps)
        {
            var branch = new FlowBranch { IsDefault = true };
            var builder = new FlowSubStepBuilder(branch);
            defineSteps(builder);
            _step.Branches.Add(branch);
            return this;
        }
    }
}
