using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Base;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Domain.Models;
using MongoDB.Driver;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Infrastructure.Services.Base
{
    public class ResilienceService<T> : IResilienceService<T>
    {
        private readonly ILoggingService _logger;

        private static readonly ActivitySource ActivitySource = new("BaseService");
        private static readonly Meter Meter = new("BaseService.Metrics");
        private static readonly Counter<long> OperationCounter = Meter.CreateCounter<long>("base_service_operations_total");
        private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>("base_service_operation_duration_seconds");
        private static readonly Counter<long> ErrorCounter = Meter.CreateCounter<long>("base_service_errors_total");

        public ResilienceService(ILoggingService logger)
        {
            _logger = logger;
        }

        // Generic builder pattern for complex scenarios
        public IResilienceBuilder<TResult> CreateBuilder<TResult>(Scope scope, Func<Task<TResult>> work)
        {
            var service = new ResilienceService<TResult>(_logger);
            return new ResilienceBuilder<TResult>(service, scope, work);
        }

        // Generic factory methods for common scenarios
        public static SafeExecuteOptions<T> ForDatabaseOperation<T>()
        {
            return new SafeExecuteOptions<T>().WithDatabaseResilience();
        }

        public static SafeExecuteOptions<T> ForHttpOperation<T>()
        {
            return new SafeExecuteOptions<T>().WithHttpResilience();
        }

        public static SafeExecuteOptions<T> ForCriticalOperation<T>()
        {
            return new SafeExecuteOptions<T>().WithCriticalOperationResilience();
        }

        public static SafeExecuteOptions<T> ForExchangeOperation<T>()
        {
            return new SafeExecuteOptions<T>().WithExchangeOperationResilience();
        }

        public static SafeExecuteOptions<T> ForQuickOperation<T>(TimeSpan? timeout = null)
        {
            return new SafeExecuteOptions<T>()
                .WithTimeout(timeout ?? TimeSpan.FromSeconds(3))
                .WithRetry(2)
                .WithLightweightInstrumentation(); // Skip heavy logging/metrics
        }

        // ⚡ NEW: Lightweight execution for internal operations
        public async Task<TResult> ExecuteDirectly<TResult>(Func<Task<TResult>> work)
        {
            try
            {
                return await work();
            }
            catch (Exception ex)
            {
                // Convert to domain exceptions if needed
                throw ConvertToDomainException(ex);
            }
        }

        // ⚡ OPTIMIZED: Main SafeExecute with conditional instrumentation
        public async Task<ResultWrapper<TResult>> SafeExecute<TResult>(
            Scope scope,
            Func<Task<TResult>> work,
            SafeExecuteOptions<TResult>? options = null)
        {
            // 🚀 FAST PATH: Skip instrumentation for lightweight operations
            if (options?.IsLightweight == true)
            {
                return await ExecuteLightweight(work, options, scope);
            }

            // 🔧 MEDIUM PATH: Essential instrumentation only
            if (options?.EnableDetailedInstrumentation != true)
            {
                return await ExecuteWithEssentialInstrumentation(work, options, scope);
            }

            // 📊 FULL PATH: Complete instrumentation (existing behavior)
            return await ExecuteWithFullInstrumentation(work, options, scope);
        }

        // ⚡ FAST PATH: Minimal overhead execution
        private async Task<ResultWrapper<TResult>> ExecuteLightweight<TResult>(
            Func<Task<TResult>> work,
            SafeExecuteOptions<TResult>? options,
            Scope scope)
        {
            try
            {
                var result = await ExecuteWithResilience(work, options, scope);
                return result;
            }
            catch (Exception ex)
            {
                // Only log errors for lightweight operations
                if (IsSignificantError(ex))
                {
                    _logger.LogError("Operation {OperationName} failed: {ErrorMessage}",
                        scope.OperationName, ex.Message);
                }

                return ResultWrapper<TResult>.FromException(ex, includeStackTrace: options?.IncludeStackTrace ?? false);
            }
        }

        // 🔧 MEDIUM PATH: Essential instrumentation
        private async Task<ResultWrapper<TResult>> ExecuteWithEssentialInstrumentation<TResult>(
            Func<Task<TResult>> work,
            SafeExecuteOptions<TResult>? options,
            Scope scope)
        {
            var stopwatch = Stopwatch.StartNew();
            Activity? activity = null;

            try
            {
                // Only create activity for critical operations
                if (options?.IsCritical == true || scope.LogLevel >= LogLevel.Error)
                {
                    activity = ActivitySource.StartActivity(scope.OperationName);
                    activity?.SetTag("operation.id", Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString());
                }

                // Execute with resilience
                var result = await ExecuteWithResilience(work, options, scope);

                stopwatch.Stop();

                // Only record metrics for operations that take significant time
                if (stopwatch.ElapsedMilliseconds > 100 || options?.IsCritical == true)
                {
                    OperationDuration.Record(stopwatch.Elapsed.TotalSeconds,
                        new KeyValuePair<string, object?>("operation", scope.OperationName),
                        new KeyValuePair<string, object?>("outcome", "success"));

                    activity?.SetTag("operation.outcome", "success");
                    activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);
                }

                // Execute success callback without logging overhead
                if (options?.OnSuccess != null && result.IsSuccess)
                {
                    try
                    {
                        await options.OnSuccess(result.Data);
                    }
                    catch (Exception callbackEx)
                    {
                        // Don't fail the operation due to callback failure
                        _logger.LogWarning("Success callback failed for {OperationName}: {ErrorMessage}",
                            scope.OperationName, callbackEx.Message);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Record error metrics
                ErrorCounter.Add(1,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

                activity?.SetTag("operation.outcome", "error");
                activity?.SetTag("error.type", ex.GetType().Name);

                // Only detailed error logging for significant errors
                if (IsSignificantError(ex) || scope.LogLevel >= LogLevel.Error)
                {
                    await _logger.LogTraceWithStackTraceAsync(
                        $"Operation failed: {scope.OperationName} - {ex.Message}",
                        ex,
                        scope.OperationName,
                        level: scope.LogLevel,
                        requiresResolution: ShouldRequireResolution(ex, options));
                }

                // Execute error callback
                if (options?.OnError != null)
                {
                    try
                    {
                        await options.OnError(ex);
                    }
                    catch
                    {
                        // Ignore callback failures
                    }
                }

                return ResultWrapper<TResult>.FromException(ex, includeStackTrace: options?.IncludeStackTrace ?? true);
            }
            finally
            {
                activity?.Dispose();
            }
        }

        // 📊 FULL PATH: Complete instrumentation (for critical operations only)
        private async Task<ResultWrapper<TResult>> ExecuteWithFullInstrumentation<TResult>(
            Func<Task<TResult>> work,
            SafeExecuteOptions<TResult>? options,
            Scope scope)
        {
            var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
            var operationId = Guid.NewGuid().ToString();
            var stopwatch = Stopwatch.StartNew();

            using var activity = ActivitySource.StartActivity($"{scope.NameSpace}.{scope.FileName}.{scope.OperationName}");
            activity?.SetTag("operation.id", operationId);
            activity?.SetTag("entity.type", typeof(TResult).Name);
            activity?.SetTag("scope.namespace", scope.NameSpace);
            activity?.SetTag("scope.filename", scope.FileName);
            activity?.SetTag("scope.operationname", scope.OperationName);

            var enrichedScope = scope with
            {
                State = scope.State.Union(new Dictionary<string, object>
                {
                    ["CorrelationId"] = correlationId,
                    ["OperationId"] = operationId,
                    ["StartTime"] = DateTimeOffset.UtcNow,
                    ["EntityType"] = typeof(TResult).Name
                }).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            try
            {
                using var loggerScope = _logger.BeginScope(enrichedScope);

                OperationCounter.Add(1,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("entity_type", typeof(TResult).Name));

                await _logger.LogTraceAsync(
                    $"Starting operation: {scope.OperationName}",
                    scope.OperationName,
                    level: LogLevel.Information);

                var result = await ExecuteWithResilience(work, options, enrichedScope);

                stopwatch.Stop();
                var duration = stopwatch.Elapsed;
                var durationSeconds = duration.TotalSeconds;

                // Performance threshold checking
                if (options?.PerformanceThreshold.HasValue == true && duration > options.PerformanceThreshold.Value)
                {
                    options.OnSlowOperation?.Invoke(duration);

                    await _logger.LogTraceAsync(
                        $"Slow operation detected: {scope.OperationName} took {duration.TotalSeconds:F2}s",
                        scope.OperationName,
                        level: LogLevel.Warning);
                }

                // Success metrics and logging
                OperationDuration.Record(durationSeconds,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("entity_type", typeof(TResult).Name),
                    new KeyValuePair<string, object?>("outcome", "success"));

                activity?.SetTag("operation.outcome", "success");
                activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

                await _logger.LogTraceAsync(
                    $"Operation completed successfully: {scope.OperationName} (Duration: {duration.TotalSeconds:F2}s)",
                    scope.OperationName,
                    level: LogLevel.Information);

                // Execute success callback
                if (options?.OnSuccess != null && result.IsSuccess)
                {
                    await ExecuteCallback(() => options.OnSuccess(result.Data), "OnSuccess", enrichedScope);
                }

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var duration = stopwatch.Elapsed.TotalSeconds;

                ErrorCounter.Add(1,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("entity_type", typeof(TResult).Name),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

                OperationDuration.Record(duration,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("entity_type", typeof(TResult).Name),
                    new KeyValuePair<string, object?>("outcome", "error"));

                activity?.SetTag("operation.outcome", "error");
                activity?.SetTag("error.type", ex.GetType().Name);
                activity?.SetTag("error.message", ex.Message);
                activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

                await _logger.LogTraceWithStackTraceAsync(
                    $"Operation failed: {scope.OperationName} - {ex.Message}",
                    ex,
                    scope.OperationName,
                    level: scope.LogLevel,
                    requiresResolution: ShouldRequireResolution(ex, options));

                if (options?.OnError != null)
                {
                    await ExecuteCallback(() => options.OnError(ex), "OnError", enrichedScope);
                }

                return ResultWrapper<TResult>.FromException(ex, includeStackTrace: true);
            }
        }

        private async Task<ResultWrapper<T>> ExecuteWithResilience<T>(
            Func<Task<T>> work,
            SafeExecuteOptions<T>? options,
            Scope scope)
        {
            try
            {
                if (options?.ResiliencePipeline == null)
                {
                    var callbackResult = await work();
                    return ResultWrapper<T>.Success(callbackResult);
                }

                var result = await options.ResiliencePipeline.ExecuteAsync(async (cancellationToken) =>
                {
                    return await work();
                }, CancellationToken.None);

                return ResultWrapper<T>.Success(result);
            }
            catch (BrokenCircuitException ex)
            {
                // Only log circuit breaker events if they're significant
                if (options?.IsLightweight != true)
                {
                    await _logger.LogTraceAsync(
                        $"Circuit breaker open for operation: {scope.OperationName}",
                        scope.OperationName,
                        level: LogLevel.Error,
                        requiresResolution: true);
                }

                throw new ServiceUnavailableException($"Service temporarily unavailable: {scope.OperationName}");
            }
            catch (TimeoutRejectedException ex)
            {
                if (options?.IsLightweight != true)
                {
                    await _logger.LogTraceAsync(
                        $"Operation timeout for: {scope.OperationName}",
                        scope.OperationName,
                        level: LogLevel.Error,
                        requiresResolution: true);
                }

                throw new TimeoutException($"Operation timeout: {scope.OperationName}");
            }
        }

        private async Task ExecuteCallback(Func<Task> asyncCallback, string callbackName, Scope scope)
        {
            try
            {
                await asyncCallback();
            }
            catch (Exception callbackEx)
            {
                await _logger.LogTraceAsync(
                    $"Callback {callbackName} failed in operation {scope.OperationName}: {callbackEx.Message}",
                    scope.OperationName,
                    level: LogLevel.Warning);
            }
        }

        // ⚡ Helper methods for performance optimization
        private static bool IsSignificantError(Exception ex)
        {
            return ex is DatabaseException or
                   TimeoutException or
                   ServiceUnavailableException or
                   BrokenCircuitException or
                   ValidationException { ValidationErrors.Count: > 0 };
        }

        private static Exception ConvertToDomainException(Exception ex)
        {
            // Convert common exceptions to domain exceptions
            return ex switch
            {
                ArgumentNullException => new ValidationException(ex.Message, []),
                ArgumentException => new ValidationException(ex.Message, []),
                InvalidOperationException => new ValidationException(ex.Message, []),
                _ => ex
            };
        }

        private static bool ShouldRequireResolution<TResult>(Exception ex, SafeExecuteOptions<TResult>? options)
        {
            if (options?.RequireResolutionPredicate != null)
                return options.RequireResolutionPredicate(ex);

            return ex is DatabaseException or
                   TimeoutException or
                   ServiceUnavailableException or
                   BrokenCircuitException;
        }

        // Non-generic builder for operations that don't return data
        public IResilienceBuilder CreateBuilder(Scope scope, Func<Task> work)
        {
            return new ResilienceBuilder(this, scope, work);
        }

        // Non-generic factory methods
        public static SafeExecuteOptions ForDatabaseOperation()
        {
            return new SafeExecuteOptions().WithDatabaseResilience();
        }

        public static SafeExecuteOptions ForHttpOperation()
        {
            return new SafeExecuteOptions().WithHttpResilience();
        }

        public static SafeExecuteOptions ForCriticalOperation()
        {
            return new SafeExecuteOptions().WithCriticalOperationResilience();
        }

        public static SafeExecuteOptions ForExchangeOperation()
        {
            return new SafeExecuteOptions().WithExchangeOperationResilience();
        }

        public static SafeExecuteOptions ForQuickOperation(TimeSpan? timeout = null)
        {
            return new SafeExecuteOptions()
                .WithTimeout(timeout ?? TimeSpan.FromSeconds(3))
                .WithRetry(2)
                .WithLightweightInstrumentation();
        }

        // Non-generic SafeExecute method (similar optimizations apply)
        public async Task<ResultWrapper> SafeExecute(
            Scope scope,
            Func<Task> work,
            SafeExecuteOptions? options = null)
        {
            // Apply similar optimization patterns as the generic version
            if (options?.IsLightweight == true)
            {
                try
                {
                    await ExecuteWithResilienceVoid(work, options, scope);
                    return ResultWrapper.Success();
                }
                catch (Exception ex)
                {
                    if (IsSignificantError(ex))
                    {
                        _logger.LogError("Operation {OperationName} failed: {ErrorMessage}",
                            scope.OperationName, ex.Message);
                    }
                    return ResultWrapper.FromException(ex, includeStackTrace: options?.IncludeStackTrace ?? false);
                }
            }

            // Continue with existing implementation for non-lightweight operations...
            var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
            var operationId = Guid.NewGuid().ToString();
            var stopwatch = Stopwatch.StartNew();

            using var activity = ActivitySource.StartActivity($"{scope.NameSpace}.{scope.FileName}.{scope.OperationName}");
            activity?.SetTag("operation.id", operationId);
            activity?.SetTag("scope.namespace", scope.NameSpace);
            activity?.SetTag("scope.filename", scope.FileName);
            activity?.SetTag("scope.operationname", scope.OperationName);

            var enrichedScope = scope with
            {
                State = scope.State.Union(new Dictionary<string, object>
                {
                    ["CorrelationId"] = correlationId,
                    ["OperationId"] = operationId,
                    ["StartTime"] = DateTimeOffset.UtcNow
                }).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            try
            {
                using var loggerScope = _logger.BeginScope(enrichedScope);

                OperationCounter.Add(1,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("entity_type", "void"));

                await _logger.LogTraceAsync(
                    $"Starting operation: {scope.OperationName}",
                    scope.OperationName,
                    level: LogLevel.Information);

                await ExecuteWithResilienceVoid(work, options, enrichedScope);

                stopwatch.Stop();
                var duration = stopwatch.Elapsed;
                var durationSeconds = duration.TotalSeconds;

                if (options?.PerformanceThreshold.HasValue == true && duration > options.PerformanceThreshold.Value)
                {
                    options.OnSlowOperation?.Invoke(duration);

                    await _logger.LogTraceAsync(
                        $"Slow operation detected: {scope.OperationName} took {duration.TotalSeconds:F2}s",
                        scope.OperationName,
                        level: LogLevel.Warning);
                }

                OperationDuration.Record(durationSeconds,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("entity_type", "void"),
                    new KeyValuePair<string, object?>("outcome", "success"));

                activity?.SetTag("operation.outcome", "success");
                activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

                await _logger.LogTraceAsync(
                    $"Operation completed successfully: {scope.OperationName} (Duration: {duration.TotalSeconds:F2}s)",
                    scope.OperationName,
                    level: LogLevel.Information);

                if (options?.OnSuccess != null)
                {
                    await ExecuteCallbackVoid(() => options.OnSuccess(), "OnSuccess", enrichedScope);
                }

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var duration = stopwatch.Elapsed.TotalSeconds;

                ErrorCounter.Add(1,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("entity_type", "void"),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

                OperationDuration.Record(duration,
                    new KeyValuePair<string, object?>("operation", scope.OperationName),
                    new KeyValuePair<string, object?>("entity_type", "void"),
                    new KeyValuePair<string, object?>("outcome", "error"));

                activity?.SetTag("operation.outcome", "error");
                activity?.SetTag("error.type", ex.GetType().Name);
                activity?.SetTag("error.message", ex.Message);
                activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

                await _logger.LogTraceWithStackTraceAsync(
                    $"Operation failed: {scope.OperationName} - {ex.Message}",
                    ex,
                    scope.OperationName,
                    level: scope.LogLevel,
                    requiresResolution: ShouldRequireResolutionVoid(ex, options));

                if (options?.OnError != null)
                {
                    await ExecuteCallbackVoid(() => options.OnError(ex), "OnError", enrichedScope);
                }

                return ResultWrapper.FromException(ex, includeStackTrace: true);
            }
        }

        private async Task ExecuteWithResilienceVoid(
            Func<Task> work,
            SafeExecuteOptions? options,
            Scope scope)
        {
            try
            {
                if (options?.ResiliencePipeline == null)
                {
                    await work();
                    return;
                }

                await options.ResiliencePipeline.ExecuteAsync(async (cancellationToken) =>
                {
                    await work();
                }, CancellationToken.None);
            }
            catch (BrokenCircuitException ex)
            {
                if (options?.IsLightweight != true)
                {
                    await _logger.LogTraceAsync(
                        $"Circuit breaker open for operation: {scope.OperationName}",
                        scope.OperationName,
                        level: LogLevel.Error,
                        requiresResolution: true);
                }

                throw new ServiceUnavailableException($"Service temporarily unavailable: {scope.OperationName}");
            }
            catch (TimeoutRejectedException ex)
            {
                if (options?.IsLightweight != true)
                {
                    await _logger.LogTraceAsync(
                        $"Operation timeout for: {scope.OperationName}",
                        scope.OperationName,
                        level: LogLevel.Error,
                        requiresResolution: true);
                }

                throw new TimeoutException($"Operation timeout: {scope.OperationName}");
            }
        }

        private async Task ExecuteCallbackVoid(Func<Task> asyncCallback, string callbackName, Scope scope)
        {
            try
            {
                await asyncCallback();
            }
            catch (Exception callbackEx)
            {
                await _logger.LogTraceAsync(
                    $"Callback {callbackName} failed in operation {scope.OperationName}: {callbackEx.Message}",
                    scope.OperationName,
                    level: LogLevel.Warning);
            }
        }

        private static bool ShouldRequireResolutionVoid(Exception ex, SafeExecuteOptions? options)
        {
            if (options?.RequireResolutionPredicate != null)
                return options.RequireResolutionPredicate(ex);

            return ex is DatabaseException or
                   TimeoutException or
                   ServiceUnavailableException or
                   BrokenCircuitException;
        }
    }
}