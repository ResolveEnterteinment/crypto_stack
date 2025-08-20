namespace Infrastructure.Services.FlowEngine.Models
{
    public abstract class FlowDefinition
    {
        public string FlowId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public FlowStatus Status { get; set; }
        public string CurrentStepName { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = new();

        // Pause/Resume state
        public DateTime? PausedAt { get; set; }
        public PauseReason? PauseReason { get; set; }
        public string? PauseMessage { get; set; }

        // FIXED: Add version for optimistic concurrency control
        public int Version { get; set; } = 1;

        public abstract void SetData<T>(T data) where T : class;
    }
}
