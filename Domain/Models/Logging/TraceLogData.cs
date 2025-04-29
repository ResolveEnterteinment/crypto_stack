
using Microsoft.Extensions.Logging;

namespace Domain.Models.Logging
{
    public class TraceLog : BaseEntity
    {
        public Guid? CorrelationId { get; set; }
        public Guid? ParentCorrelationId { get; set; }            // Optional parent ID
        public string Operation { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public LogLevel Level { get; set; } = LogLevel.Information;
        public Dictionary<string, string>? Context { get; set; }
        public bool RequiresResolution { get; set; } = false;
        public string? ResolutionStatus { get; set; } = null; // ["Unresolved", "Acknowledged", "Reconciled"]
        public string? ResolutionComment { get; set; } = null;
    }
}
