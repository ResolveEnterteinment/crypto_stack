using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.PauseResume
{
    /// <summary>
    /// Resume condition for automatic checking
    /// </summary>
    public class ResumeCondition
    {
        public string FlowId { get; set; }
        public Func<FlowContext, Task<bool>> Condition { get; set; }
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);
        public DateTime NextCheck { get; set; } = DateTime.UtcNow;
        public int MaxRetries { get; set; } = -1; // -1 = infinite
        public int CurrentRetries { get; set; } = 0;
    }
}
