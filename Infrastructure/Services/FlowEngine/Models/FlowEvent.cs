namespace Infrastructure.Services.FlowEngine.Models
{
    /// <summary>
    /// Flow event for audit trail
    /// </summary>
    public sealed record FlowEvent
    {
        public string EventId { get; init; } = Guid.NewGuid().ToString();
        public string FlowId { get; init; } = string.Empty;
        public FlowEventType EventType { get; init; }
        public string UserId { get; init; } = string.Empty;
        public object Data { get; init; } = new();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }
}
