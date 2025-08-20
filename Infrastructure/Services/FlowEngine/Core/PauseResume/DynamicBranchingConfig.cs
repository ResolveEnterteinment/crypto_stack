using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.PauseResume
{
    /// <summary>
    /// Configuration for dynamic sub-step generation
    /// </summary>
    public class DynamicBranchingConfig
    {
        public Func<FlowContext, IEnumerable<object>> DataSelector { get; set; }
        public Func<object, int, FlowSubStep> StepFactory { get; set; }
        public ExecutionStrategy ExecutionStrategy { get; set; } = ExecutionStrategy.Parallel;
        public int MaxConcurrency { get; set; } = 10;
        public TimeSpan BatchDelay { get; set; } = TimeSpan.Zero;
    }
}
