using Polly;

namespace Domain.DTOs.Base
{
    public class SafeExecuteOptions
    {
        public bool IsLightweight { get; set; } = true;
        public bool EnableDetailedInstrumentation { get; set; } = false;
        public bool IsCritical { get; set; } = false;
        public bool IncludeStackTrace { get; set; } = true;
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

        public SafeExecuteOptions WithLightweightInstrumentation()
        {
            IsLightweight = true;
            EnableDetailedInstrumentation = false;
            IncludeStackTrace = false;
            return this;
        }

        public SafeExecuteOptions WithEssentialInstrumentation()
        {
            IsLightweight = false;
            EnableDetailedInstrumentation = false;
            IncludeStackTrace = true;
            return this;
        }

        public SafeExecuteOptions WithFullInstrumentation()
        {
            IsLightweight = false;
            EnableDetailedInstrumentation = true;
            IncludeStackTrace = true;
            IsCritical = true;
            return this;
        }
    }

    public class SafeExecuteOptions<T>
    {
        public bool IsLightweight { get; set; } = true;
        public bool EnableDetailedInstrumentation { get; set; } = false;
        public bool IsCritical { get; set; } = false;
        public bool IncludeStackTrace { get; set; } = true;
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

        public SafeExecuteOptions<T> WithLightweightInstrumentation()
        {
            IsLightweight = true;
            EnableDetailedInstrumentation = false;
            IncludeStackTrace = false;
            return this;
        }

        public SafeExecuteOptions<T> WithEssentialInstrumentation()
        {
            IsLightweight = false;
            EnableDetailedInstrumentation = false;
            IncludeStackTrace = true;
            return this;
        }

        public SafeExecuteOptions<T> WithFullInstrumentation()
        {
            IsLightweight = false;
            EnableDetailedInstrumentation = true;
            IncludeStackTrace = true;
            IsCritical = true;
            return this;
        }
    }

    public class AlertThreshold
    {
        public TimeSpan Duration { get; set; }
        public int ErrorCount { get; set; }
        public string? AlertChannel { get; set; }
    }
}