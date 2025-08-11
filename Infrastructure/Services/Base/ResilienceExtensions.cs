using Domain.DTOs.Base;
using Domain.Exceptions;
using MongoDB.Driver;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Net.Sockets;
using System.Threading;
using System.Threading.RateLimiting;

namespace Infrastructure.Services.Base
{
    public static class ResilienceExtensions
    {
        #region Generic Extensions (existing)

        public static SafeExecuteOptions<T> WithRetry<T>(this SafeExecuteOptions<T> options, int maxAttempts = 3)
        {
            options.ResiliencePipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200),
                    MaxDelay = TimeSpan.FromSeconds(5)
                })
                .Build();
            return options;
        }

        public static SafeExecuteOptions<T> WithCircuitBreaker<T>(this SafeExecuteOptions<T> options)
        {
            options.ResiliencePipeline = new ResiliencePipelineBuilder()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(30),
                    FailureRatio = 0.3,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(60)
                })
                .Build();
            return options;
        }

        public static SafeExecuteOptions<T> WithTimeout<T>(this SafeExecuteOptions<T> options, TimeSpan timeout)
        {
            options.ResiliencePipeline = new ResiliencePipelineBuilder()
                .AddTimeout(timeout)
                .Build();
            return options;
        }

        public static SafeExecuteOptions<T> WithQuickOperationResilience<T>(this SafeExecuteOptions<T> options, TimeSpan timeout, int maxAttempts = 2)
        {
            options.ResiliencePipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200),
                    MaxDelay = TimeSpan.FromSeconds(5)
                })
                .AddTimeout(timeout)
                .Build();
            return options;
        }

        public static SafeExecuteOptions<T> WithComprehensiveResilience<T>(
            this SafeExecuteOptions<T> options,
            int maxRetries = 3,
            TimeSpan? timeout = null,
            double failureRatio = 0.5)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(500),
                    MaxDelay = TimeSpan.FromSeconds(10)
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(30),
                    FailureRatio = failureRatio,
                    MinimumThroughput = 10,
                    SamplingDuration = TimeSpan.FromSeconds(60)
                });

            if (timeout.HasValue)
            {
                builder.AddTimeout(timeout.Value);
            }

            options.ResiliencePipeline = builder.Build();
            return options;
        }

        public static SafeExecuteOptions<T> WithMongoDbResilience<T>(this SafeExecuteOptions<T> options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 5, // Higher for transient MongoDB issues
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(100), // Start fast for MongoDB
                    MaxDelay = TimeSpan.FromSeconds(8), // Reasonable max for DB ops
                    ShouldHandle = new PredicateBuilder()
                        // MongoDB-specific retryable exceptions
                        .Handle<MongoConnectionException>()
                        .Handle<MongoException>(ex => IsRetryableMongoException(ex))
                        .Handle<MongoWriteException>(ex => IsRetryableWriteException(ex))
                        .Handle<MongoCommandException>(ex => IsRetryableCommandException(ex))
                        .Handle<MongoServerException>(ex => IsRetryableServerException(ex))
                        .Handle<TimeoutException>()
                        .Handle<SocketException>()
                        .Handle<IOException>(),
                    OnRetry = args =>
                    {
                        // Enhanced retry logging with MongoDB context
                        Task.Run(async () =>
                        {
                            var exception = args.Outcome.Exception;
                            var mongoContext = ExtractMongoContext(exception);

                            // Log with MongoDB-specific details
                            Console.WriteLine($"MongoDB retry {args.AttemptNumber}/5: {exception?.GetType().Name} - {mongoContext}");
                        });
                        return default;
                    }
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(15), // Shorter for DB - recover faster
                    FailureRatio = 0.6, // Higher tolerance for MongoDB
                    MinimumThroughput = 5, // Lower threshold for DB operations
                    SamplingDuration = TimeSpan.FromSeconds(30), // Shorter sampling window
                    ShouldHandle = new PredicateBuilder()
                        .Handle<MongoConnectionException>()
                        .Handle<MongoException>()
                        .Handle<MongoServerException>()
                        .Handle<TimeoutException>()
                })
                .AddTimeout(TimeSpan.FromSeconds(10)) // Aggressive timeout for DB
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Optimized patterns for different operation types
        public static SafeExecuteOptions<T> WithMongoDbReadResilience<T>(this SafeExecuteOptions<T> options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3, // Fewer retries for reads
                    BackoffType = DelayBackoffType.Linear, // Linear backoff for reads
                    Delay = TimeSpan.FromMilliseconds(50),
                    MaxDelay = TimeSpan.FromSeconds(4),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<MongoConnectionException>()
                        .Handle<MongoException>(ex => IsRetryableMongoException(ex))
                        .Handle<MongoServerException>(ex => IsRetryableServerException(ex))
                        .Handle<TimeoutException>()
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(10),
                    FailureRatio = 0.7, // Higher tolerance for reads
                    MinimumThroughput = 3
                })
                .AddTimeout(TimeSpan.FromSeconds(5)) // Shorter timeout for reads
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        public static SafeExecuteOptions<T> WithMongoDbWriteResilience<T>(this SafeExecuteOptions<T> options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 5, // More retries for writes
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200), // Longer initial delay
                    MaxDelay = TimeSpan.FromSeconds(10),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<MongoConnectionException>()
                        .Handle<MongoException>(ex => IsRetryableMongoException(ex))
                        .Handle<MongoWriteException>(ex => IsRetryableWriteException(ex))
                        .Handle<MongoServerException>(ex => IsRetryableServerException(ex))
                        .Handle<MongoCommandException>(ex => IsRetryableCommandException(ex))
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(20),
                    FailureRatio = 0.5, // Stricter for writes
                    MinimumThroughput = 5
                })
                .AddTimeout(TimeSpan.FromSeconds(15)) // Longer timeout for writes
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Update your existing database resilience to use MongoDB-specific pattern
        public static SafeExecuteOptions<T> WithDatabaseResilience<T>(this SafeExecuteOptions<T> options)
        {
            return options.WithMongoDbResilience(); // Delegate to MongoDB-specific implementation
        }

        // Add HTTP/API specific resilience
        public static SafeExecuteOptions<T> WithHttpResilience<T>(this SafeExecuteOptions<T> options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(500),
                    MaxDelay = TimeSpan.FromSeconds(10),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutException>()
                        .Handle<TaskCanceledException>()
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(60),
                    FailureRatio = 0.6,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(120)
                })
                .AddTimeout(TimeSpan.FromSeconds(30))
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Add critical operation resilience (more aggressive)
        public static SafeExecuteOptions<T> WithCriticalOperationResilience<T>(this SafeExecuteOptions<T> options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 5,
                    BackoffType = DelayBackoffType.Linear,
                    UseJitter = false,
                    Delay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromSeconds(2)
                })
                .AddTimeout(TimeSpan.FromSeconds(60))
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Add exchange operation specific resilience
        public static SafeExecuteOptions<T> WithExchangeOperationResilience<T>(
            this SafeExecuteOptions<T> options)
        {
            var builder = new ResiliencePipelineBuilder()
                // Add rate limiting for exchange APIs
                .AddRateLimiter(new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 1200, // Binance allows 1200 requests per minute
                    SegmentsPerWindow = 2, // Required: divide the window into segments
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10
                }))
                // Improved retry strategy for exchange APIs
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3, // Reduced for rate-limited APIs
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(2), // Longer initial delay
                    MaxDelay = TimeSpan.FromSeconds(30),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<ExchangeApiException>(ex =>
                            ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("server error", StringComparison.OrdinalIgnoreCase))
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        // Add exponential backoff for rate limits
                        if (args.Outcome.Exception?.Message?.Contains("rate limit") == true)
                        {
                            return new ValueTask(Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber))));
                        }
                        return default;
                    }
                })
                // More conservative circuit breaker for exchanges
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromMinutes(5), // Longer for external APIs
                    FailureRatio = 0.4, // More sensitive to failures
                    MinimumThroughput = 10, // Higher threshold
                    SamplingDuration = TimeSpan.FromMinutes(2)
                })
                // Add bulkhead isolation
                .AddConcurrencyLimiter(10) // Limit concurrent exchange calls
                .AddTimeout(TimeSpan.FromSeconds(30))
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Add performance monitoring
        public static SafeExecuteOptions<T> WithPerformanceMonitoring<T>(
            this SafeExecuteOptions<T> options,
            TimeSpan warningThreshold,
            TimeSpan errorThreshold)
        {
            options.PerformanceThreshold = warningThreshold;
            options.OnSlowOperation = duration =>
            {
                if (duration > errorThreshold)
                {
                    // Could trigger alerts, metrics, etc.
                    Console.WriteLine($"CRITICAL: Operation took {duration.TotalSeconds:F2}s (threshold: {errorThreshold.TotalSeconds:F2}s)");
                }
                else
                {
                    Console.WriteLine($"WARNING: Slow operation detected: {duration.TotalSeconds:F2}s");
                }
            };
            return options;
        }

        #endregion

        #region Non-Generic Extensions (new)

        public static SafeExecuteOptions WithRetry(this SafeExecuteOptions options, int maxAttempts = 3)
        {
            options.ResiliencePipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200),
                    MaxDelay = TimeSpan.FromSeconds(5)
                })
                .Build();
            return options;
        }

        public static SafeExecuteOptions WithCircuitBreaker(this SafeExecuteOptions options)
        {
            options.ResiliencePipeline = new ResiliencePipelineBuilder()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(30),
                    FailureRatio = 0.3,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(60)
                })
                .Build();
            return options;
        }

        public static SafeExecuteOptions WithTimeout(this SafeExecuteOptions options, TimeSpan timeout)
        {
            options.ResiliencePipeline = new ResiliencePipelineBuilder()
                .AddTimeout(timeout)
                .Build();
            return options;
        }

        public static SafeExecuteOptions WithQuickOperationResilience(this SafeExecuteOptions options, TimeSpan timeout, int maxAttempts = 2)
        {
            options.ResiliencePipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200),
                    MaxDelay = TimeSpan.FromSeconds(5)
                })
                .AddTimeout(timeout)
                .Build();
            return options;
        }

        public static SafeExecuteOptions WithComprehensiveResilience(
            this SafeExecuteOptions options,
            int maxRetries = 3,
            TimeSpan? timeout = null,
            double failureRatio = 0.5)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(500),
                    MaxDelay = TimeSpan.FromSeconds(10)
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(30),
                    FailureRatio = failureRatio,
                    MinimumThroughput = 10,
                    SamplingDuration = TimeSpan.FromSeconds(60)
                });

            if (timeout.HasValue)
            {
                builder.AddTimeout(timeout.Value);
            }

            options.ResiliencePipeline = builder.Build();
            return options;
        }

        public static SafeExecuteOptions WithMongoDbResilience(this SafeExecuteOptions options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 5, // Higher for transient MongoDB issues
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(100), // Start fast for MongoDB
                    MaxDelay = TimeSpan.FromSeconds(8), // Reasonable max for DB ops
                    ShouldHandle = new PredicateBuilder()
                        // MongoDB-specific retryable exceptions
                        .Handle<MongoConnectionException>()
                        .Handle<MongoException>(ex => IsRetryableMongoException(ex))
                        .Handle<MongoWriteException>(ex => IsRetryableWriteException(ex))
                        .Handle<MongoCommandException>(ex => IsRetryableCommandException(ex))
                        .Handle<MongoServerException>(ex => IsRetryableServerException(ex))
                        .Handle<TimeoutException>()
                        .Handle<SocketException>()
                        .Handle<IOException>(),
                    OnRetry = args =>
                    {
                        // Enhanced retry logging with MongoDB context
                        Task.Run(async () =>
                        {
                            var exception = args.Outcome.Exception;
                            var mongoContext = ExtractMongoContext(exception);

                            // Log with MongoDB-specific details
                            Console.WriteLine($"MongoDB retry {args.AttemptNumber}/5: {exception?.GetType().Name} - {mongoContext}");
                        });
                        return default;
                    }
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(15), // Shorter for DB - recover faster
                    FailureRatio = 0.6, // Higher tolerance for MongoDB
                    MinimumThroughput = 5, // Lower threshold for DB operations
                    SamplingDuration = TimeSpan.FromSeconds(30), // Shorter sampling window
                    ShouldHandle = new PredicateBuilder()
                        .Handle<MongoConnectionException>()
                        .Handle<MongoException>()
                        .Handle<MongoServerException>()
                        .Handle<TimeoutException>()
                })
                .AddTimeout(TimeSpan.FromSeconds(10)) // Aggressive timeout for DB
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Optimized patterns for different operation types
        public static SafeExecuteOptions WithMongoDbReadResilience(this SafeExecuteOptions options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3, // Fewer retries for reads
                    BackoffType = DelayBackoffType.Linear, // Linear backoff for reads
                    Delay = TimeSpan.FromMilliseconds(50),
                    MaxDelay = TimeSpan.FromSeconds(2),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<MongoConnectionException>()
                        .Handle<MongoException>(ex => IsRetryableMongoException(ex))
                        .Handle<MongoServerException>(ex => IsRetryableServerException(ex))
                        .Handle<TimeoutException>()
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(10),
                    FailureRatio = 0.7, // Higher tolerance for reads
                    MinimumThroughput = 3
                })
                .AddTimeout(TimeSpan.FromSeconds(5)) // Shorter timeout for reads
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        public static SafeExecuteOptions WithMongoDbWriteResilience(this SafeExecuteOptions options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 5, // More retries for writes
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200), // Longer initial delay
                    MaxDelay = TimeSpan.FromSeconds(10),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<MongoConnectionException>()
                        .Handle<MongoException>(ex => IsRetryableMongoException(ex))
                        .Handle<MongoWriteException>(ex => IsRetryableWriteException(ex))
                        .Handle<MongoServerException>(ex => IsRetryableServerException(ex))
                        .Handle<MongoCommandException>(ex => IsRetryableCommandException(ex))
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(20),
                    FailureRatio = 0.5, // Stricter for writes
                    MinimumThroughput = 5
                })
                .AddTimeout(TimeSpan.FromSeconds(15)) // Longer timeout for writes
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Update your existing database resilience to use MongoDB-specific pattern
        public static SafeExecuteOptions WithDatabaseResilience(this SafeExecuteOptions options)
        {
            return options.WithMongoDbResilience(); // Delegate to MongoDB-specific implementation
        }

        // Add HTTP/API specific resilience
        public static SafeExecuteOptions WithHttpResilience(this SafeExecuteOptions options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(500),
                    MaxDelay = TimeSpan.FromSeconds(10),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutException>()
                        .Handle<TaskCanceledException>()
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(60),
                    FailureRatio = 0.6,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(120)
                })
                .AddTimeout(TimeSpan.FromSeconds(30))
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Add critical operation resilience (more aggressive)
        public static SafeExecuteOptions WithCriticalOperationResilience(this SafeExecuteOptions options)
        {
            var builder = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 5,
                    BackoffType = DelayBackoffType.Linear,
                    UseJitter = false,
                    Delay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromSeconds(2)
                })
                .AddTimeout(TimeSpan.FromSeconds(60))
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Add exchange operation specific resilience
        public static SafeExecuteOptions WithExchangeOperationResilience(this SafeExecuteOptions options)
        {
            var builder = new ResiliencePipelineBuilder()
                // Add rate limiting for exchange APIs
                .AddRateLimiter(new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 1200, // Binance allows 1200 requests per minute
                    SegmentsPerWindow = 2, // Required: divide the window into segments
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10
                }))
                // Improved retry strategy for exchange APIs
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3, // Reduced for rate-limited APIs
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(2), // Longer initial delay
                    MaxDelay = TimeSpan.FromSeconds(30),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<ExchangeApiException>(ex =>
                            ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("server error", StringComparison.OrdinalIgnoreCase))
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        // Add exponential backoff for rate limits
                        if (args.Outcome.Exception?.Message?.Contains("rate limit") == true)
                        {
                            return new ValueTask(Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber))));
                        }
                        return default;
                    }
                })
                // More conservative circuit breaker for exchanges
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromMinutes(5), // Longer for external APIs
                    FailureRatio = 0.4, // More sensitive to failures
                    MinimumThroughput = 10, // Higher threshold
                    SamplingDuration = TimeSpan.FromMinutes(2)
                })
                // Add bulkhead isolation
                .AddConcurrencyLimiter(10) // Limit concurrent exchange calls
                .AddTimeout(TimeSpan.FromSeconds(30))
                .Build();

            options.ResiliencePipeline = builder;
            return options;
        }

        // Add performance monitoring
        public static SafeExecuteOptions WithPerformanceMonitoring(
            this SafeExecuteOptions options,
            TimeSpan warningThreshold,
            TimeSpan errorThreshold)
        {
            options.PerformanceThreshold = warningThreshold;
            options.OnSlowOperation = duration =>
            {
                if (duration > errorThreshold)
                {
                    // Could trigger alerts, metrics, etc.
                    Console.WriteLine($"CRITICAL: Operation took {duration.TotalSeconds:F2}s (threshold: {errorThreshold.TotalSeconds:F2}s)");
                }
                else
                {
                    Console.WriteLine($"WARNING: Slow operation detected: {duration.TotalSeconds:F2}s");
                }
            };
            return options;
        }

        #endregion

        #region MongoDB Helper Methods (shared by both generic and non-generic)

        // MongoDB error classification helpers using correct MongoDB driver API
        private static bool IsRetryableMongoException(MongoException ex)
        {
            // Check common MongoDB exception patterns
            var message = ex.Message?.ToLowerInvariant() ?? "";

            return message.Contains("timeout") ||
                   message.Contains("network") ||
                   message.Contains("connection") ||
                   message.Contains("not master") ||
                   message.Contains("primary stepped down") ||
                   message.Contains("interrupted") ||
                   message.Contains("shutdown");
        }

        private static bool IsRetryableServerException(MongoServerException ex)
        {
            // For MongoServerException, check the message content since Code property may not be available
            var message = ex.Message?.ToLowerInvariant() ?? "";

            return message.Contains("timeout") ||
                   message.Contains("network") ||
                   message.Contains("not master") ||
                   message.Contains("primary stepped down") ||
                   message.Contains("interrupted") ||
                   message.Contains("shutdown") ||
                   message.Contains("host unreachable") ||
                   message.Contains("host not found");
        }

        private static bool IsRetryableWriteException(MongoWriteException ex)
        {
            if (ex.WriteError == null) return false;

            var errorCode = ex.WriteError.Code;
            var message = ex.WriteError.Message?.ToLowerInvariant() ?? "";

            // Don't retry duplicate key errors
            if (errorCode == 11000 || errorCode == 11001 || message.Contains("duplicate"))
                return false;

            return errorCode switch
            {
                50 => true,     // MaxTimeMSExpired
                89 => true,     // NetworkTimeout
                91 => true,     // ShutdownInProgress
                189 => true,    // PrimarySteppedDown
                10107 => true,  // NotMaster
                _ => message.Contains("timeout") ||
                     message.Contains("network") ||
                     message.Contains("not master") ||
                     message.Contains("primary stepped down")
            };
        }

        private static bool IsRetryableCommandException(MongoCommandException ex)
        {
            // Check both error code and message for command exceptions
            var message = ex.Message?.ToLowerInvariant() ?? "";

            // Try to get error code if available, fallback to message parsing
            try
            {
                // Some versions expose different properties
                var errorCode = ex.GetType().GetProperty("Code")?.GetValue(ex) as int?;

                if (errorCode.HasValue)
                {
                    return errorCode.Value switch
                    {
                        50 => true,     // MaxTimeMSExpired
                        89 => true,     // NetworkTimeout
                        91 => true,     // ShutdownInProgress
                        189 => true,    // PrimarySteppedDown
                        10107 => true,  // NotMaster
                        216 => true,    // InterruptedAtShutdown
                        238 => true,    // InterruptedDueToReplStateChange
                        _ => false
                    };
                }
            }
            catch
            {
                // Fall back to message-based detection
            }

            return message.Contains("timeout") ||
                   message.Contains("network") ||
                   message.Contains("not master") ||
                   message.Contains("primary stepped down") ||
                   message.Contains("interrupted") ||
                   message.Contains("shutdown");
        }

        private static string ExtractMongoContext(Exception? exception)
        {
            return exception switch
            {
                MongoWriteException mwe => $"Write: Code={mwe.WriteError?.Code}, Message={mwe.WriteError?.Message}",
                MongoCommandException mcme => $"Command: {mcme.Message}",
                MongoConnectionException mce => $"Connection: {mce.ConnectionId?.ServerId}",
                MongoServerException mse => $"Server: {mse.Message}",
                MongoException me => $"Mongo: {me.Message}",
                _ => exception?.Message ?? "Unknown"
            };
        }

        #endregion
    }
}