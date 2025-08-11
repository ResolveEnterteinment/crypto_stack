using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Base;
using Domain.DTOs.Logging;

namespace Infrastructure.Services.Base
{
    /// <summary>
    /// Fluent builder for configuring resilience options (non-generic).
    /// </summary>
    public class ResilienceBuilder : IResilienceBuilder
    {
        private readonly IResilienceService _service;
        private readonly Scope _scope;
        private readonly Func<Task> _work;
        private readonly SafeExecuteOptions _options;

        internal ResilienceBuilder(
            IResilienceService service,
            Scope scope,
            Func<Task> work)
        {
            _service = service;
            _scope = scope;
            _work = work;
            _options = new SafeExecuteOptions();
        }

        public IResilienceBuilder WithRetry(int maxAttempts = 3)
        {
            _options.WithRetry(maxAttempts);
            return this;
        }

        public IResilienceBuilder WithCircuitBreaker()
        {
            _options.WithCircuitBreaker();
            return this;
        }

        public IResilienceBuilder WithTimeout(TimeSpan timeout)
        {
            _options.WithTimeout(timeout);
            return this;
        }

        public IResilienceBuilder WithQuickOperationResilience(TimeSpan? timeout = null, int maxAttempts = 2)
        {
            _options.WithQuickOperationResilience(timeout ?? TimeSpan.FromSeconds(2), maxAttempts);
            return this;
        }

        public IResilienceBuilder WithComprehensiveResilience(
            int maxRetries = 3,
            TimeSpan? timeout = null,
            double failureRatio = 0.5)
        {
            _options.WithComprehensiveResilience(maxRetries, timeout, failureRatio);
            return this;
        }

        public IResilienceBuilder WithMongoDbReadResilience()
        {
            _options.WithMongoDbReadResilience();
            return this;
        }

        public IResilienceBuilder WithMongoDbWriteResilience()
        {
            _options.WithMongoDbWriteResilience();
            return this;
        }

        public IResilienceBuilder WithMongoDbResilience()
        {
            _options.WithMongoDbResilience();
            return this;
        }

        public IResilienceBuilder WithDatabaseResilience()
        {
            _options.WithDatabaseResilience();
            return this;
        }

        public IResilienceBuilder WithHttpResilience()
        {
            _options.WithHttpResilience();
            return this;
        }

        public IResilienceBuilder WithCriticalOperationResilience()
        {
            _options.WithCriticalOperationResilience();
            return this;
        }

        public IResilienceBuilder WithExchangeOperationResilience()
        {
            _options.WithExchangeOperationResilience();
            return this;
        }

        public IResilienceBuilder WithPerformanceThreshold(
            TimeSpan threshold,
            Action<TimeSpan>? onSlowOperation = null)
        {
            _options.PerformanceThreshold = threshold;
            _options.OnSlowOperation = onSlowOperation;
            return this;
        }

        public IResilienceBuilder WithPerformanceMonitoring(
            TimeSpan warningThreshold,
            TimeSpan errorThreshold)
        {
            _options.WithPerformanceMonitoring(warningThreshold, errorThreshold);
            return this;
        }

        public IResilienceBuilder OnSuccess(Func<Task> callback)
        {
            _options.OnSuccess = callback;
            return this;
        }

        public IResilienceBuilder OnError(Func<Exception, Task> callback)
        {
            _options.OnError = callback;
            return this;
        }

        public IResilienceBuilder OnCriticalError(Func<Exception, Task> callback)
        {
            _options.OnCriticalError = callback;
            return this;
        }

        public IResilienceBuilder WithAdditionalContext(Dictionary<string, object> context)
        {
            _options.AdditionalContext = context;
            return this;
        }

        public IResilienceBuilder WithContext(string key, object value)
        {
            _options.AdditionalContext ??= new Dictionary<string, object>();
            _options.AdditionalContext[key] = value;
            return this;
        }

        public IResilienceBuilder WithResolutionDelegate(Func<Exception, bool> resolutionDelegate)
        {
            _options.RequireResolutionPredicate = resolutionDelegate;
            return this;
        }

        public async Task<ResultWrapper> ExecuteAsync()
        {
            return await _service.SafeExecute(_scope, _work, _options);
        }
    }

    /// <summary>
    /// Fluent builder for configuring resilience options.
    /// </summary>
    /// <typeparam name="TResult">Entity type</typeparam>
    public class ResilienceBuilder<TResult> : IResilienceBuilder<TResult>
    {
        private readonly IResilienceService<TResult> _service; // Changed from IResilienceService<object>
        private readonly Scope _scope;
        private readonly Func<Task<TResult>> _work;
        private readonly SafeExecuteOptions<TResult> _options;

        internal ResilienceBuilder(
            IResilienceService<TResult> service,  // Already correct
            Scope scope,
            Func<Task<TResult>> work)
        {
            _service = service;
            _scope = scope;
            _work = work;
            _options = new SafeExecuteOptions<TResult>();
        }

        public IResilienceBuilder<TResult> WithRetry(int maxAttempts = 3)
        {
            _options.WithRetry(maxAttempts);
            return this;
        }

        public IResilienceBuilder<TResult> WithCircuitBreaker()
        {
            _options.WithCircuitBreaker();
            return this;
        }

        public IResilienceBuilder<TResult> WithTimeout(TimeSpan timeout)
        {
            _options.WithTimeout(timeout);
            return this;
        }

        public IResilienceBuilder<TResult> WithQuickOperationResilience(TimeSpan? timeout = null, int maxAttempts = 2)
        {
            _options.WithQuickOperationResilience(timeout ?? TimeSpan.FromSeconds(2), maxAttempts);
            return this;
        }

        public IResilienceBuilder<TResult> WithComprehensiveResilience(
            int maxRetries = 3,
            TimeSpan? timeout = null,
            double failureRatio = 0.5)
        {
            _options.WithComprehensiveResilience(maxRetries, timeout, failureRatio);
            return this;
        }

        public IResilienceBuilder<TResult> WithMongoDbReadResilience()
        {
            _options.WithMongoDbReadResilience();
            return this;
        }

        public IResilienceBuilder<TResult> WithMongoDbWriteResilience()
        {
            _options.WithMongoDbWriteResilience();
            return this;
        }

        public IResilienceBuilder<TResult> WithMongoDbResilience()
        {
            _options.WithMongoDbResilience();
            return this;
        }

        public IResilienceBuilder<TResult> WithDatabaseResilience()
        {
            _options.WithDatabaseResilience();
            return this;
        }

        public IResilienceBuilder<TResult> WithHttpResilience()
        {
            _options.WithHttpResilience();
            return this;
        }

        public IResilienceBuilder<TResult> WithCriticalOperationResilience()
        {
            _options.WithCriticalOperationResilience();
            return this;
        }

        public IResilienceBuilder<TResult> WithExchangeOperationResilience()
        {
            _options.WithExchangeOperationResilience();
            return this;
        }

        public IResilienceBuilder<TResult> WithPerformanceThreshold(
            TimeSpan threshold,
            Action<TimeSpan>? onSlowOperation = null)
        {
            _options.PerformanceThreshold = threshold;
            _options.OnSlowOperation = onSlowOperation;
            return this;
        }

        public IResilienceBuilder<TResult> WithPerformanceMonitoring(
            TimeSpan warningThreshold,
            TimeSpan errorThreshold)
        {
            _options.WithPerformanceMonitoring(warningThreshold, errorThreshold);
            return this;
        }

        public IResilienceBuilder<TResult> OnSuccess(Func<TResult, Task> callback)
        {
            _options.OnSuccess = callback;
            return this;
        }

        public IResilienceBuilder<TResult> OnError(Func<Exception, Task> callback)
        {
            _options.OnError = callback;
            return this;
        }

        public IResilienceBuilder<TResult> OnCriticalError(Func<Exception, Task> callback)
        {
            _options.OnCriticalError = callback;
            return this;
        }

        public IResilienceBuilder<TResult> WithAdditionalContext(Dictionary<string, object> context)
        {
            _options.AdditionalContext = context;
            return this;
        }

        public IResilienceBuilder<TResult> WithContext(string key, object value)
        {
            _options.AdditionalContext ??= new Dictionary<string, object>();
            _options.AdditionalContext[key] = value;
            return this;
        }

        public IResilienceBuilder<TResult> WithResolutionDelegate(Func<Exception, bool> resolutionDelegate)
        {
            _options.RequireResolutionPredicate = resolutionDelegate;
            return this;
        }


        public async Task<ResultWrapper<TResult>> ExecuteAsync()
        {
            return await _service.SafeExecute(_scope, _work, _options);
        }
    }
}