using Domain.DTOs;
using Domain.DTOs.Logging;

namespace Application.Interfaces.Base
{
    /// <summary>
    /// Interface for building resilience configurations using a fluent API (non-generic).
    /// </summary>
    public interface IResilienceBuilder
    {
        /// <summary>
        /// Configures retry behavior for the operation.
        /// </summary>
        /// <param name="maxAttempts">Maximum number of retry attempts (default: 3)</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder WithRetry(int maxAttempts = 3);

        /// <summary>
        /// Configures circuit breaker behavior for the operation.
        /// </summary>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder WithCircuitBreaker();

        /// <summary>
        /// Configures timeout behavior for the operation.
        /// </summary>
        /// <param name="timeout">Maximum duration before timing out</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder WithTimeout(TimeSpan timeout);

        IResilienceBuilder WithQuickOperationResilience(TimeSpan? timeout = null, int maxAttempts = 2);

        /// <summary>
        /// Configures comprehensive resilience with retry, circuit breaker, and optional timeout.
        /// </summary>
        /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
        /// <param name="timeout">Optional timeout duration</param>
        /// <param name="failureRatio">Circuit breaker failure ratio threshold (default: 0.5)</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder WithComprehensiveResilience(
            int maxRetries = 3,
            TimeSpan? timeout = null,
            double failureRatio = 0.5);

        IResilienceBuilder WithMongoDbReadResilience();
        IResilienceBuilder WithMongoDbWriteResilience();
        IResilienceBuilder WithMongoDbResilience();
        IResilienceBuilder WithDatabaseResilience();
        IResilienceBuilder WithHttpResilience();
        IResilienceBuilder WithCriticalOperationResilience();
        IResilienceBuilder WithExchangeOperationResilience();

        /// <summary>
        /// Configures performance monitoring with thresholds.
        /// </summary>
        /// <param name="threshold">Performance warning threshold</param>
        /// <param name="onSlowOperation">Optional callback for slow operations</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder WithPerformanceThreshold(
            TimeSpan threshold,
            Action<TimeSpan>? onSlowOperation = null);

        /// <summary>
        /// Configures performance monitoring with warning and error thresholds.
        /// </summary>
        /// <param name="warningThreshold">Threshold for warning level performance</param>
        /// <param name="errorThreshold">Threshold for error level performance</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder WithPerformanceMonitoring(
            TimeSpan warningThreshold,
            TimeSpan errorThreshold);

        /// <summary>
        /// Configures a success callback to be executed when the operation succeeds.
        /// </summary>
        /// <param name="callback">Async callback to execute on success</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder OnSuccess(Func<Task> callback);

        /// <summary>
        /// Configures an error callback to be executed when the operation fails.
        /// </summary>
        /// <param name="callback">Async callback to execute on error</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder OnError(Func<Exception, Task> callback);

        /// <summary>
        /// Configures a critical error callback for severe failures.
        /// </summary>
        /// <param name="callback">Async callback to execute on critical errors</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder OnCriticalError(Func<Exception, Task> callback);

        /// <summary>
        /// Adds additional context data to the operation.
        /// </summary>
        /// <param name="context">Additional context data</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder WithAdditionalContext(Dictionary<string, object> context);

        /// <summary>
        /// Adds a single context value to the operation.
        /// </summary>
        /// <param name="key">Context key</param>
        /// <param name="value">Context value</param>
        /// <returns>The builder instance for method chaining</returns>
        IResilienceBuilder WithContext(string key, object value);

        IResilienceBuilder WithResolutionDelegate(Func<Exception, bool> resolutionDelegate);

        /// <summary>
        /// Executes the configured operation asynchronously.
        /// </summary>
        /// <returns>A task representing the result of the operation</returns>
        Task<ResultWrapper> ExecuteAsync();
    }

    /// <summary>
    /// Generic interface for building resilience configurations using a fluent API.
    /// </summary>
    /// <typeparam name="T">Entity type inheriting from BaseEntity</typeparam>
    public interface IResilienceBuilder<T>
    {
        IResilienceBuilder<T> WithRetry(int maxAttempts = 3);
        IResilienceBuilder<T> WithCircuitBreaker();
        IResilienceBuilder<T> WithTimeout(TimeSpan timeout);
        IResilienceBuilder<T> WithQuickOperationResilience(TimeSpan? timeout = null, int maxAttempts = 2);
        IResilienceBuilder<T> WithComprehensiveResilience(int maxRetries = 3, TimeSpan? timeout = null, double failureRatio = 0.5);
        IResilienceBuilder<T> WithMongoDbReadResilience();
        IResilienceBuilder<T> WithMongoDbWriteResilience();
        IResilienceBuilder<T> WithMongoDbResilience();
        IResilienceBuilder<T> WithDatabaseResilience();
        IResilienceBuilder<T> WithHttpResilience();
        IResilienceBuilder<T> WithCriticalOperationResilience();
        IResilienceBuilder<T> WithExchangeOperationResilience();
        IResilienceBuilder<T> WithPerformanceThreshold(TimeSpan threshold, Action<TimeSpan>? onSlowOperation = null);
        IResilienceBuilder<T> WithPerformanceMonitoring(TimeSpan warningThreshold, TimeSpan errorThreshold);
        IResilienceBuilder<T> OnSuccess(Func<T, Task> callback);
        IResilienceBuilder<T> OnError(Func<Exception, Task> callback);
        IResilienceBuilder<T> OnCriticalError(Func<Exception, Task> callback);
        IResilienceBuilder<T> WithAdditionalContext(Dictionary<string, object> context);
        IResilienceBuilder<T> WithContext(string key, object value);
        IResilienceBuilder<T> WithResolutionDelegate(Func<Exception, bool> resolutionDelegate);
        Task<ResultWrapper<T>> ExecuteAsync();
    }
}