using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.DTOs.Logging;
using Domain.Models.Logging;
using Infrastructure.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Serilog.Context;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Infrastructure.Services.Logging
{
    public class LoggingService : ILoggingService
    {
        private readonly ILogger<LoggingService> _logger;
        private readonly IHttpContextAccessor _httpAccessor;
        private readonly AsyncLocal<Dictionary<string, object?>> _currentContext = new();

        private readonly ICrudRepository<TraceLogData> Repository;

        public IDictionary<string, object?> Context
            => _currentContext.Value ?? new Dictionary<string, object?>();

        public LoggingService(
            ILogger<LoggingService> logger,
            IHttpContextAccessor httpAccessor,
            ICrudRepository<TraceLogData> repository)
        {
            _logger = logger;
            _httpAccessor = httpAccessor;
            Repository = repository;
        }

        public IDisposable BeginScope() =>
            BeginScope(null, null);
        public IDisposable BeginScope(object? state = null) =>
            BeginScope(null, state);
        public IDisposable BeginScope(Scope scope) =>
            BeginScope(scope.OperationName, scope.State);
        public IDisposable BeginScope(string? operationName, object? state)
        {
            try
            {
                // 1) Determine the parentCorrelationId from our last scope (if any)
                string? parentCorrelationId = null;
                if (_currentContext.Value != null &&
                    _currentContext.Value.TryGetValue("correlation.id", out var prev) &&
                    prev is string prevId &&
                    !string.IsNullOrWhiteSpace(prevId))
                {
                    parentCorrelationId = prevId;
                }

                // 2) Generate a new correlationId for this scope
                var correlationId = Guid.NewGuid().ToString();

                // 3) Build the tag set
                var tags = ConvertStateToDictionary(state) ?? new Dictionary<string, object?>();
                tags["correlation.id"] = correlationId;
                if (!string.IsNullOrWhiteSpace(parentCorrelationId))
                    tags["parent.correlation.id"] = parentCorrelationId;
                if (!string.IsNullOrWhiteSpace(operationName))
                    tags["operation"] = operationName;

                // 4) Store into our AsyncLocal for the next nested scope
                _currentContext.Value = new Dictionary<string, object?>(tags);

                // 5) Propagate into CorrelationContext so GetCorrelation picks it up
                CorrelationContext.Set(Guid.Parse(correlationId),
                                       string.IsNullOrWhiteSpace(parentCorrelationId)
                                         ? null
                                         : Guid.Parse(parentCorrelationId));

                // 6) (Optional) also update HttpContext.Items
                var http = _httpAccessor.HttpContext;
                if (http != null)
                {
                    http.Items["CorrelationId"] = correlationId;
                    http.Items["ParentCorrelationId"] = parentCorrelationId;
                    foreach (var item in tags)
                    {
                        http.Items[item.Key] = item.Value;
                    }
                }

                // 7) Start our own Activity so tags flow into LogTraceAsync
                var activity = ActivityHelper.StartActivity(
                    operationName ?? Activity.Current?.OperationName ?? "UnnamedOperation",
                    tags.ToDictionary(k => k.Key, v => v.Value));

                // 8) Push all tags into Serilog's LogContext
                var disposables = new List<IDisposable>
                {
                    LogContext.PushProperty("correlation.id",        correlationId),
                    LogContext.PushProperty("parent.correlation.id", parentCorrelationId ?? string.Empty)
                };
                foreach (var kv in tags)
                    disposables.Add(LogContext.PushProperty(kv.Key, kv.Value ?? "null"));

                var scopeName = operationName ??
                    $"{_httpAccessor.HttpContext?.Request.RouteValues["controller"]}/{_httpAccessor.HttpContext?.Request.RouteValues["action"]}" ??
                    "UnnamedActivity";
                LogTraceAsync($"Begin scope {scopeName}", level: Domain.Constants.Logging.LogLevel.Trace).GetAwaiter().GetResult();
                return new DisposableCollection(disposables);
            }
            catch (Exception)
            {
                throw;
            }

        }

        public IDisposable EnrichScope(params (string Key, object? Value)[] properties)
        {
            return EnrichScope(properties.ToDictionary());
        }
        public IDisposable EnrichScope(IDictionary<string, object?>? properties)
        {
            try
            {
                if (properties == null || !properties.Any())
                    return new DisposableCollection(new List<IDisposable>());

                var disposables = new List<IDisposable>();
                var activity = Activity.Current;

                foreach (var (key, value) in properties)
                {
                    if (!string.IsNullOrWhiteSpace(key) && value != null)
                    {
                        disposables.Add(LogContext.PushProperty(key, value));
                        activity?.SetTag(key, value.ToString());

                        if (_currentContext.Value != null) _currentContext.Value[key] = value;
                    }
                }

                var scopeName = activity?.DisplayName ?? $"{_httpAccessor.HttpContext?.Request.RouteValues["controller"]}/{_httpAccessor.HttpContext?.Request.RouteValues["action"]}";
                LogTraceAsync($"Enrich scope {scopeName} ", level: Domain.Constants.Logging.LogLevel.Trace).GetAwaiter().GetResult();

                return new DisposableCollection(disposables);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task LogTraceAsync(string message, string? action = null, Domain.Constants.Logging.LogLevel? level = Domain.Constants.Logging.LogLevel.Information, bool requiresResolution = false, HttpContext? context = null)
        {
            // Call the enhanced version with null values for the caller attributes
            await LogTraceWithStackTraceAsync(
                message: message,
                exception: null,
                action: action,
                level: level,
                requiresResolution: requiresResolution,
                context: context);
        }

        public async Task LogTraceWithStackTraceAsync(
            string message,
            Exception? exception = null,
            string? action = null,
            Domain.Constants.Logging.LogLevel? level = Domain.Constants.Logging.LogLevel.Information,
            bool requiresResolution = false,
            HttpContext? context = null,
            [CallerMemberName] string? callerMemberName = null,
            [CallerFilePath] string? callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = 0)
        {
            try
            {
                // Safely get correlation - handle potential null values
                var correlation = GetCorrelation();
                if (correlation == null)
                {
                    // Fallback correlation if GetCorrelation fails
                    correlation = new CorrelationInfo
                    {
                        CorrelationId = Guid.NewGuid(),
                        ParentCorrelationId = null
                    };
                }

                context ??= _httpAccessor?.HttpContext;

                var activity = Activity.Current;
                var operation = activity?.DisplayName ?? "UnnamedOperation";

                // Collect Context Details - with null safety
                var contextData = new Dictionary<string, string>();

                // Safely handle HttpContext
                if (context != null)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(context.Request?.Path))
                            contextData["Path"] = context.Request.Path.ToString();
                        if (!string.IsNullOrEmpty(context.Request?.Method))
                            contextData["Method"] = context.Request.Method;

                        if (context.User?.Identity?.IsAuthenticated == true)
                        {
                            contextData["User"] = context.User.Identity.Name ?? "Unknown";
                        }

                        if (context.Items != null && context.Items.Any())
                        {
                            foreach (var item in context.Items)
                            {
                                if (item.Key != null && item.Value != null)
                                {
                                    var keyStr = item.Key.ToString();
                                    var valueStr = item.Value.ToString();
                                    if (!string.IsNullOrEmpty(keyStr) && !string.IsNullOrEmpty(valueStr))
                                    {
                                        contextData[keyStr] = valueStr;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception contextEx)
                    {
                        // Log context extraction error but don't fail the whole logging operation
                        contextData["ContextExtractionError"] = contextEx.Message;
                    }
                }

                // Safely handle Activity tags
                if (activity?.Tags != null)
                {
                    try
                    {
                        foreach (var tag in activity.Tags)
                        {
                            if (!string.IsNullOrEmpty(tag.Key) && !string.IsNullOrEmpty(tag.Value) && !contextData.ContainsKey(tag.Key))
                                contextData[tag.Key] = tag.Value;
                        }
                    }
                    catch (Exception tagEx)
                    {
                        contextData["ActivityTagExtractionError"] = tagEx.Message;
                    }
                }

                // Add correlation data safely
                contextData["correlation.id"] = correlation.CorrelationId.ToString();
                if (correlation.ParentCorrelationId.HasValue)
                    contextData["parent.correlation.id"] = correlation.ParentCorrelationId.Value.ToString();

                var log = new TraceLogData
                {
                    CorrelationId = correlation.CorrelationId,
                    ParentCorrelationId = correlation.ParentCorrelationId,
                    Message = message ?? "null",
                    Level = level ?? Domain.Constants.Logging.LogLevel.Information,
                    Operation = operation ?? "UnknownOperation",
                    Context = contextData.Any() ? contextData : null,
                    RequiresResolution = requiresResolution
                };

                // Capture stack trace and exception information for Error and Critical levels
                if (level == Domain.Constants.Logging.LogLevel.Error || level == Domain.Constants.Logging.LogLevel.Critical)
                {
                    // Add caller information safely
                    log.CallerMemberName = callerMemberName;
                    log.CallerFilePath = callerFilePath;
                    log.CallerLineNumber = callerLineNumber;

                    try
                    {
                        // Capture stack trace
                        if (exception != null)
                        {
                            log.StackTrace = exception.StackTrace;
                            log.ExceptionType = exception.GetType().FullName;

                            // Capture inner exception details
                            if (exception.InnerException != null)
                            {
                                log.InnerException = $"{exception.InnerException.GetType().FullName}: {exception.InnerException.Message}";
                            }
                        }
                        else
                        {
                            // Capture current stack trace if no exception provided
                            var stackTrace = new StackTrace(true);
                            log.StackTrace = stackTrace.ToString();
                        }
                    }
                    catch (Exception stackEx)
                    {
                        // If stack trace capture fails, log the error but continue
                        log.StackTrace = $"Failed to capture stack trace: {stackEx.Message}";
                    }
                }

                // Safely log to Serilog
                try
                {
                    _logger?.Log(LogLevel.Trace, "{@TraceLog}", log);
                }
                catch (Exception serilogEx)
                {
                    // If Serilog fails, continue with database logging
                    System.Diagnostics.Debug.WriteLine($"Serilog failed: {serilogEx.Message}");
                }

                // Safely insert to database
                if (Repository != null)
                {
                    var insertResult = await Repository.InsertAsync(log);
                    if (insertResult == null || !insertResult.IsSuccess)
                    {
                        // Don't throw here as it could cause infinite recursion
                        System.Diagnostics.Debug.WriteLine($"Failed to insert trace log: {insertResult?.ErrorMessage ?? "Insert returned null"}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Last resort error handling - use basic logger and don't throw
                try
                {
                    _logger?.LogError(ex, "Critical error in LogTraceWithStackTraceAsync for message: {Message}", message);
                }
                catch
                {
                    // If even basic logging fails, write to debug output
                    System.Diagnostics.Debug.WriteLine($"Critical logging failure: {ex}");
                }

                // Don't re-throw to prevent infinite recursion or application crash
            }
        }

        public void LogInformation(string message) => _logger.LogInformation(message);
        public void LogInformation(string message, params object?[] args) => _logger.LogInformation(message, args);
        public void LogWarning(string message) => _logger.LogWarning(message);
        public void LogWarning(string message, params object?[] args) => _logger.LogWarning(message, args);

        public void LogError(string message) => _logger.LogError(message);
        public void LogError(string message, params object?[] args) => _logger.LogError(message, args);

        public void LogError(string message, Exception? exception = null, [CallerMemberName] string? callerMemberName = null, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = 0)
        {
            try
            {
                _logger.LogError(exception, message);

                // Also log to our trace system with stack trace - use Task.Run to prevent blocking
                Task.Run(async () =>
                {
                    try
                    {
                        await LogTraceWithStackTraceAsync(message, exception, "LogError", Domain.Constants.Logging.LogLevel.Error, true, null, callerMemberName, callerFilePath, callerLineNumber);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to log error trace: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to log error: {ex}");
            }
        }

        public void LogCritical(string message, Exception? exception = null, [CallerMemberName] string? callerMemberName = null, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = 0)
        {
            try
            {
                _logger.LogCritical(exception, message);

                // Also log to our trace system with stack trace - use Task.Run to prevent blocking
                Task.Run(async () =>
                {
                    try
                    {
                        await LogTraceWithStackTraceAsync(message, exception, "LogCritical", Domain.Constants.Logging.LogLevel.Critical, true, null, callerMemberName, callerFilePath, callerLineNumber);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to log critical trace: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to log critical: {ex}");
            }
        }

        private CorrelationInfo GetCorrelation()
        {
            try
            {
                // 1) FIRST look at your global CorrelationContext
                if (CorrelationContext.Current is { } ctx)
                    return ctx;

                // 2) FALL BACK to the HTTP‐level items (set in your TraceContextMiddleware)
                var correlationId = _httpAccessor?.HttpContext?.Items["CorrelationId"] as string;
                var parentId = _httpAccessor?.HttpContext?.Items["ParentCorrelationId"] as string;

                if (string.IsNullOrEmpty(correlationId))
                    correlationId = Guid.NewGuid().ToString();

                return new CorrelationInfo
                {
                    CorrelationId = Guid.Parse(correlationId),
                    ParentCorrelationId = !string.IsNullOrEmpty(parentId) ? Guid.Parse(parentId) : null
                };
            }
            catch (Exception ex)
            {
                // Return a safe fallback correlation
                System.Diagnostics.Debug.WriteLine($"Failed to get correlation: {ex}");
                return new CorrelationInfo
                {
                    CorrelationId = Guid.NewGuid(),
                    ParentCorrelationId = null
                };
            }
        }

        private static IDictionary<string, object?>? ConvertStateToDictionary(object? state)
        {
            try
            {
                if (state == null) return null;
                if (state is IEnumerable<KeyValuePair<string, object?>> dict)
                    return dict.ToDictionary(x => x.Key, x => x.Value);
                return state.GetType()
                            .GetProperties()
                            .ToDictionary(p => p.Name, p => p.GetValue(state));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to convert state to dictionary: {ex}");
                return new Dictionary<string, object?> { ["ConversionError"] = ex.Message };
            }
        }

        private class DisposableCollection : IDisposable
        {
            private readonly List<IDisposable> _disposables;
            public DisposableCollection(List<IDisposable> disposables)
                => _disposables = disposables ?? new List<IDisposable>();
            public void Dispose()
            {
                foreach (var disposable in _disposables)
                {
                    try
                    {
                        disposable?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to dispose: {ex}");
                    }
                }
            }
        }
    }
}