namespace Domain.DTOs.Flow
{
    public class FlowDetailDto
    {
        public Guid FlowId { get; set; }
        public string FlowType { get; set; }
        public string Status { get; set; }
        public string UserId { get; set; }
        public string CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? PausedAt { get; set; }
        public string CurrentStepName { get; set; }
        public int CurrentStepIndex { get; set; }
        public string PauseReason { get; set; }
        public string PauseMessage { get; set; }
        public string LastError { get; set; }
        public List<StepDto> Steps { get; set; } = [];
        public List<FlowEventDto> Events { get; set; } = [];
        public Dictionary<string, object> Data { get; set; } = [];
        public int TotalSteps { get; set; }
        public TriggeredFlowDataDto? TriggeredBy { get; set; }
    }
}
