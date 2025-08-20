using Infrastructure.Services.FlowEngine.Core.Enums;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Summary information about a flow
    /// </summary>
    public class FlowSummary
    {
        public string FlowId { get; set; }
        public string FlowType { get; set; }
        public FlowStatus Status { get; set; }
        public string UserId { get; set; }
        public string CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public string CurrentStepName { get; set; }
        public PauseReason? PauseReason { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt - StartedAt : null;
    }
}
