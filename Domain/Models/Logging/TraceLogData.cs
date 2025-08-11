using Domain.Constants.Logging;
using Domain.Models;
using MongoDB.Bson.Serialization.Attributes;

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

        /// <summary>
        /// Stack trace information for Error and Critical level logs
        /// </summary>
        public string? StackTrace { get; set; }

        /// <summary>
        /// Exception type name if the log is related to an exception
        /// </summary>
        public string? ExceptionType { get; set; }

        /// <summary>
        /// Inner exception details if available
        /// </summary>
        public string? InnerException { get; set; }

        /// <summary>
        /// The method name where the log was generated
        /// </summary>
        public string? CallerMemberName { get; set; }

        /// <summary>
        /// The file path where the log was generated
        /// </summary>
        public string? CallerFilePath { get; set; }

        /// <summary>
        /// The line number where the log was generated
        /// </summary>
        public int? CallerLineNumber { get; set; }

        public bool RequiresResolution { get; set; } = false;
        public ResolutionStatus? ResolutionStatus { get; set; } = null; // ["Unresolved", "Acknowledged", "Reconciled"]
        public string? ResolutionComment { get; set; } = null;
        public Guid? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}