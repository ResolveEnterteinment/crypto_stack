using Infrastructure.Services.FlowEngine.Models;
using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Services.FlowEngine.Configuration
{
    /// <summary>
    /// Flow Engine configuration options with validation
    /// </summary>
    public sealed record FlowEngineOptions
    {
        [Required]
        public PersistenceOptions Persistence { get; init; } = new();

        public SecurityOptions Security { get; init; } = new();
        public PerformanceOptions Performance { get; init; } = new();
        public ObservabilityOptions Observability { get; init; } = new();
        public RetryOptions DefaultRetry { get; init; } = new();
    }

    public sealed record PersistenceOptions
    {
        [Required]
        public PersistenceType Type { get; init; }

        [Required]
        public string ConnectionString { get; init; } = string.Empty;

        public string DatabaseName { get; init; } = "FlowEngine";
        public int CommandTimeoutSeconds { get; init; } = 30;
        public bool EnableRetries { get; init; } = true;
        public int MaxRetryAttempts { get; init; } = 3;
    }

    public sealed record SecurityOptions
    {
        public bool EnableEncryption { get; init; } = true;
        public bool EnableAuditLog { get; init; } = true;
        public bool RequireSignedEvents { get; init; } = true;
        public string SigningKeyName { get; init; } = "FlowEngine:SigningKey";
        public TimeSpan EventSignatureExpiry { get; init; } = TimeSpan.FromMinutes(5);
        public TimeSpan MaxExecutionTime { get; init; } = TimeSpan.FromHours(24);
        public int MaxDataSizeBytes { get; init; } = 10 * 1024 * 1024; // 10MB
    }

    public sealed record PerformanceOptions
    {
        public int MaxConcurrentFlows { get; init; } = 100;
        public TimeSpan AutoSaveInterval { get; init; } = TimeSpan.FromMinutes(1);
        public bool EnableCaching { get; init; } = true;
        public int CacheSizeLimit { get; init; } = 1000;
        public TimeSpan CacheExpiry { get; init; } = TimeSpan.FromMinutes(30);
    }

    public sealed record ObservabilityOptions
    {
        public bool EnableMetrics { get; init; } = true;
        public bool EnableTracing { get; init; } = true;
        public bool EnableDetailedLogging { get; init; } = false;
        public string ActivitySourceName { get; init; } = "FlowEngine";
        public List<string> TracingHeaders { get; init; } = new() { "correlation-id", "trace-id" };
    }

    public sealed record RetryOptions
    {
        public int MaxAttempts { get; init; } = 3;
        public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);
        public double BackoffMultiplier { get; init; } = 2.0;
    }
}
