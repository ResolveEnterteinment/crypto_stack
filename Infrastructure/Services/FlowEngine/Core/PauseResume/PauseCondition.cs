using Infrastructure.Services.FlowEngine.Core.Enums;

namespace Infrastructure.Services.FlowEngine.Core.PauseResume
{
    /// <summary>
    /// Represents a condition that can pause flow execution
    /// </summary>
    public class PauseCondition
    {
        public PauseReason Reason { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
        public bool ShouldPause { get; set; }
        public ResumeConfig ResumeConfig { get; set; }

        public static PauseCondition Pause(PauseReason reason, string message, Dictionary<string, object> data = null)
        {
            return new PauseCondition
            {
                Reason = reason,
                Message = message,
                Data = data ?? new(),
                ShouldPause = true
            };
        }

        public static PauseCondition Continue()
        {
            return new PauseCondition { ShouldPause = false };
        }
    }
}
