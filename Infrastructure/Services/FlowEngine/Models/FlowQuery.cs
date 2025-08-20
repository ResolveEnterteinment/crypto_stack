namespace Infrastructure.Services.FlowEngine.Models
{
    /// <summary>
    /// Flow query with security context
    /// </summary>
    public sealed record FlowQuery
    {
        public string? UserId { get; init; }
        public FlowStatus? Status { get; init; }
        public string? FlowType { get; init; }
        public DateTime? CreatedAfter { get; init; }
        public DateTime? CreatedBefore { get; init; }
        public string? CorrelationId { get; init; }
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;
    }
}
