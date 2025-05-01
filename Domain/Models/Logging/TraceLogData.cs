
using Domain.Constants.Logging;

namespace Domain.Models.Logging
{
    public class TraceLogData : BaseEntity
    {
        public Guid? CorrelationId { get; set; }
        public Guid? ParentCorrelationId { get; set; }            // Optional parent ID
        public string Operation { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public LogLevel Level { get; set; } = LogLevel.Information;
        public Dictionary<string, string>? Context { get; set; }
        public bool RequiresResolution { get; set; } = false;
        public ResolutionStatus? ResolutionStatus { get; set; } = null; // ["Unresolved", "Acknowledged", "Reconciled"]
        public string? ResolutionComment { get; set; } = null;
        public Guid? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}
