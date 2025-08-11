using Polly;

namespace Domain.DTOs.Base
{
    public class SafeExecuteOptions
    {
        public Func<Task>? OnSuccess { get; set; }
        public Func<Exception, Task>? OnError { get; set; }

        // Enhanced resilience options
        public ResiliencePipeline? ResiliencePipeline { get; set; }
        public Func<Exception, bool>? RequireResolutionPredicate { get; set; }

        // Explicit resilience configuration
        public int? MaxRetryAttempts { get; set; }
        public bool EnableCircuitBreaker { get; set; }
        public double? CircuitBreakerFailureRatio { get; set; }
        public TimeSpan? Timeout { get; set; }

        public bool EnableMetrics { get; set; } = true;
        public bool EnableTracing { get; set; } = true;

        // Alert and notification options
        public Func<Exception, Task>? OnCriticalError { get; set; }
        public AlertThreshold? AlertThreshold { get; set; }

        // Performance monitoring
        public TimeSpan? PerformanceThreshold { get; set; }
        public Action<TimeSpan>? OnSlowOperation { get; set; }

        // Context enrichment
        public Dictionary<string, object>? AdditionalContext { get; set; }
    }

    public class SafeExecuteOptions<T>
    {
        public Func<T, Task>? OnSuccess { get; set; }
        public Func<Exception, Task>? OnError { get; set; }

        // Enhanced resilience options
        public ResiliencePipeline? ResiliencePipeline { get; set; }
        public Func<Exception, bool>? RequireResolutionPredicate { get; set; }

        // Explicit resilience configuration
        public int? MaxRetryAttempts { get; set; }
        public bool EnableCircuitBreaker { get; set; }
        public double? CircuitBreakerFailureRatio { get; set; }
        public TimeSpan? Timeout { get; set; }

        public bool EnableMetrics { get; set; } = true;
        public bool EnableTracing { get; set; } = true;

        // Alert and notification options
        public Func<Exception, Task>? OnCriticalError { get; set; }
        public AlertThreshold? AlertThreshold { get; set; }

        // Performance monitoring
        public TimeSpan? PerformanceThreshold { get; set; }
        public Action<TimeSpan>? OnSlowOperation { get; set; }

        // Context enrichment
        public Dictionary<string, object>? AdditionalContext { get; set; }
    }

    public class AlertThreshold
    {
        public TimeSpan Duration { get; set; }
        public int ErrorCount { get; set; }
        public string? AlertChannel { get; set; }
    }
}