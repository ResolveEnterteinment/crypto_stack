using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.Builders
{
    /// <summary>
    /// Builder for sub-branching logic
    /// </summary>
    public class FlowBranchBuilder
    {
        private readonly FlowStep _step;
        private FlowBranch _branch;

        internal FlowBranchBuilder(FlowStep step)
        {
            _step = step;
        }

        public FlowBranchBuilder Branch(string name)
        {
            _branch = new FlowBranch
            {
                Name = name
            };
            return this;
        }

        /// <summary>
        /// Add a conditional branch
        /// </summary>
        public FlowBranchBuilder When(Func<FlowExecutionContext, bool> condition)
        {
            _branch.Condition = condition;
            return this;
        }

        /// <summary>
        /// Add default branch (executed if no conditions match)
        /// </summary>
        public FlowBranchBuilder Otherwise()
        {
            _branch.IsDefault = true;
            return this;
        }

        /// <summary>
        /// Add a conditional branch
        /// </summary>
        public FlowBranchBuilder WithSteps(Action<BranchStepBuilder> defineSteps)
        {
            var builder = new BranchStepBuilder(_branch);
            defineSteps(builder);
            return this;
        }

        public FlowBranchBuilder WithSourceData(object data)
        {
            _branch.SourceData = data;
            return this;
        }

        public FlowBranchBuilder WithResourceGroup(string resourceGroup)
        {
            _branch.ResourceGroup = resourceGroup;
            return this;
        }

        public FlowBranchBuilder WithPriority(int priority)
        {
            _branch.Priority = priority;
            return this;
        }

        public FlowBranch Build()
        {
            _step.Branches.Add(_branch);
            return _branch;
        }
    }
}
