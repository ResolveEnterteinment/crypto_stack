namespace Infrastructure.Services.FlowEngine.Models
{
    /// <summary>
    /// Flow timeline for audit
    /// </summary>
    public sealed record FlowTimeline
    {
        public string FlowId { get; init; } = string.Empty;
        public IReadOnlyList<FlowEvent> Events { get; init; } = Array.Empty<FlowEvent>();
        public DateTime CreatedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public TimeSpan? TotalDuration => CompletedAt?.Subtract(CreatedAt);
    }
}
