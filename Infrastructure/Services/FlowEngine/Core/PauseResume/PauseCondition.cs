using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.PauseResume
{
    /// <summary>
    /// Represents a condition that can pause flow execution
    /// </summary>
    public class PauseCondition
    {
        public PauseReason Reason { get; set; }
        public string Message { get; set; }
        public object? Data { get; set; } = new();
        public bool ShouldPause { get; set; }
        public ResumeConfig ResumeConfig { get; set; }

        public static PauseCondition Pause(PauseReason reason, string message, object? data = null)
        {
            return new PauseCondition
            {
                Reason = reason,
                Message = message,
                Data = data,
                ShouldPause = true
            };
        }

        public static PauseCondition Continue()
        {
            return new PauseCondition { ShouldPause = false };
        }
    }
}
