using Infrastructure.Services.FlowEngine.Core.Builders;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.PauseResume
{
    /// <summary>
    /// Configuration for dynamic sub-step generation
    /// </summary>
    public class DynamicBranchingConfig
    {
        public Func<FlowExecutionContext, IEnumerable<object>> DataSelector { get; set; }
        public Func<FlowBranchBuilder, object, int, FlowBranch> BranchFactory { get; set; }
        public ExecutionStrategy ExecutionStrategy { get; set; } = ExecutionStrategy.Parallel;
        public int MaxConcurrency { get; set; } = 10;
        public TimeSpan BatchDelay { get; set; } = TimeSpan.Zero;
    }
}
