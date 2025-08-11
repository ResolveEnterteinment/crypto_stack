using Domain.Constants.Logging;

namespace Domain.DTOs.Logging
{
    public record Scope
    {
        public string NameSpace { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public Dictionary<string, object> State { get; set; } = new();
        public LogLevel LogLevel { get; set; } = Constants.Logging.LogLevel.Error;

        // Enhanced properties
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? RequestId { get; set; }
        public string? SessionId { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
        public SecurityContext? SecurityContext { get; set; }
        public PerformanceMetrics? Metrics { get; set; }
    }

    public class SecurityContext
    {
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public List<string>? Roles { get; set; }
        public Dictionary<string, string>? Claims { get; set; }
    }

    public class PerformanceMetrics
    {
        public int? ExpectedDurationMs { get; set; }
        public int? MaxMemoryMb { get; set; }
        public string? SlaLevel { get; set; }
    }
}