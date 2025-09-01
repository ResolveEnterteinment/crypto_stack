namespace Domain.DTOs.Flow
{
    public class FlowSummaryDto
    {
        public Guid FlowId { get; set; }
        public string FlowType { get; set; }
        public string Status { get; set; }
        public string UserId { get; set; }
        public string CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string CurrentStepName { get; set; }
        public string PauseReason { get; set; }
        public string ErrorMessage { get; set; }
        public double? Duration { get; set; }
        public int CurrentStepIndex { get; set; }
        public int TotalSteps { get; set; }
    }
}
