using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.PauseResume
{
    /// <summary>
    /// Configuration for how a paused flow can be resumed
    /// </summary>
    public class ResumeConfig
    {
        public bool AllowManualResume { get; set; } = true;
        public List<string> AllowedRoles { get; set; } = new();
        public List<EventTrigger> EventTriggers { get; set; } = new();
        public Func<FlowContext, Task<bool>> AutoResumeCondition { get; set; }
        public TimeSpan ConditionCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan? TimeoutDuration { get; set; }
        public bool ResumeOnTimeout { get; set; } = false;
    }
}
