using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Models.Logging;
using Infrastructure.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Serilog.Context;
using System.Diagnostics;

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
        public IDisposable BeginScope(string? operationName, object? state)
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
            }

            // 7) Start our own Activity so tags flow into LogTraceAsync
            var activity = ActivityHelper.StartActivity(
                operationName ?? Activity.Current?.DisplayName ?? "UnnamedOperation",
                tags.ToDictionary(k => k.Key, v => v.Value));

            // 8) Push all tags into Serilog’s LogContext
            var disposables = new List<IDisposable>
        {
            LogContext.PushProperty("correlation.id",        correlationId),
            LogContext.PushProperty("parent.correlation.id", parentCorrelationId ?? string.Empty)
        };
            foreach (var kv in tags)
                disposables.Add(LogContext.PushProperty(kv.Key, kv.Value ?? "null"));
            var scopeName = operationName ?? $"{_httpAccessor.HttpContext.Request.RouteValues["controller"]}/{_httpAccessor.HttpContext.Request.RouteValues["action"]}";
            LogTraceAsync($"Started scope {scopeName}", level: Domain.Constants.Logging.LogLevel.Trace).GetAwaiter().GetResult();
            return new DisposableCollection(disposables);
        }

        public IDisposable EnrichScope(params (string Key, object? Value)[] properties)
        {
            return EnrichScope(properties.ToDictionary());
        }
        public IDisposable EnrichScope(IDictionary<string, object?>? properties)
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

                    _currentContext.Value[key] = value;
                }
            }

            var scopeName = activity?.DisplayName ?? $"{_httpAccessor.HttpContext?.Request.RouteValues["controller"]}/{_httpAccessor.HttpContext?.Request.RouteValues["action"]}";
            LogTraceAsync($"Enriching scope {scopeName} ", level: Domain.Constants.Logging.LogLevel.Trace).GetAwaiter().GetResult();

            return new DisposableCollection(disposables);
        }

        public async Task LogTraceAsync(string message, string? action = null, Domain.Constants.Logging.LogLevel level = Domain.Constants.Logging.LogLevel.Information, bool requiresResolution = false, HttpContext? context = null)
        {
            var correlation = GetCorrelation();
            context ??= _httpAccessor.HttpContext;

            var activity = Activity.Current;
            var operation = activity?.DisplayName ?? "UnnamedOperation";

            // Collect Context Details
            var contextData = new Dictionary<string, string>();

            if (context != null)
            {
                if (!string.IsNullOrEmpty(context.Request?.Path))
                    contextData["Path"] = context.Request.Path;
                if (!string.IsNullOrEmpty(context.Request?.Method))
                    contextData["Method"] = context.Request.Method;

                if (context.User?.Identity?.IsAuthenticated == true)
                {
                    contextData["User"] = context.User.Identity.Name ?? "Unknown";
                }
            }

            if (activity != null)
            {
                foreach (var tag in activity.Tags)
                {
                    if (!contextData.ContainsKey(tag.Key))
                        contextData[tag.Key] = tag.Value;
                }
            }

            contextData["correlation.id"] = correlation.CorrelationId.ToString();
            if (correlation.ParentCorrelationId.HasValue)
                contextData["parent.correlation.id"] = correlation.ParentCorrelationId.Value.ToString();
            else
                contextData.Remove("parent.correlation.id");

            var log = new TraceLogData
            {
                CorrelationId = correlation?.CorrelationId,
                ParentCorrelationId = correlation?.ParentCorrelationId,
                Message = message,
                Level = level,
                Operation = operation,
                Context = contextData.Any() ? contextData : null,
                RequiresResolution = requiresResolution
            };

            _logger.Log(LogLevel.Trace, "{@TraceLog}", log);
            await Repository.InsertAsync(log);
        }

        public void LogInformation(string message) => _logger.LogInformation(message);
        public void LogInformation(string message, params object?[] args) => _logger.LogInformation(message, args);
        public void LogWarning(string message) => _logger.LogWarning(message);
        public void LogWarning(string message, params object?[] args) => _logger.LogWarning(message, args);
        public void LogError(string message) => _logger.LogError(message);
        public void LogError(string message, params object?[] args) => _logger.LogError(message, args);

        private CorrelationInfo GetCorrelation()
        {
            // 1) FIRST look at your global CorrelationContext
            if (CorrelationContext.Current is { } ctx)
                return ctx;

            // 2) FALL BACK to the HTTP‐level items (set in your TraceContextMiddleware)
            var correlationId = _httpAccessor.HttpContext?.Items["CorrelationId"] as string;
            var parentId = _httpAccessor.HttpContext?.Items["ParentCorrelationId"] as string;

            if (string.IsNullOrEmpty(correlationId))
                correlationId = Guid.NewGuid().ToString();

            return new CorrelationInfo
            {
                CorrelationId = Guid.Parse(correlationId),
                ParentCorrelationId = !string.IsNullOrEmpty(parentId) ? Guid.Parse(parentId) : null
            };
        }

        private static IDictionary<string, object?>? ConvertStateToDictionary(object? state)
        {
            if (state == null) return null;
            if (state is IEnumerable<KeyValuePair<string, object?>> dict)
                return dict.ToDictionary(x => x.Key, x => x.Value);
            return state.GetType()
                        .GetProperties()
                        .ToDictionary(p => p.Name, p => p.GetValue(state));
        }

        private class DisposableCollection : IDisposable
        {
            private readonly List<IDisposable> _disposables;
            public DisposableCollection(List<IDisposable> disposables)
                => _disposables = disposables;
            public void Dispose()
                => _disposables.ForEach(d => d.Dispose());
        }
    }
}
