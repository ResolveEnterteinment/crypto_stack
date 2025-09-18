namespace Domain.DTOs.Flow
{
    public class TriggeredFlowDataDto
    {
        public string Type { get; set; }
        public Guid? FlowId { get; set; }
        public string? TriggeredByStep { get; set; }
        public string? Status { get; set; } // Current status of the triggered flow
        public DateTime? CreatedAt { get; set; } // When the triggered flow was created
    }
}