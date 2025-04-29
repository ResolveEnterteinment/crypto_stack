using Microsoft.AspNetCore.Http;

namespace Application.Interfaces.Logging
{
    public interface ILoggingService
    {
        IDictionary<string, object?> Context { get; }
        IDisposable BeginScope();
        IDisposable BeginScope(object? state = null);
        IDisposable BeginScope(string? operationName = null, object? state = null);
        IDisposable EnrichScope(IDictionary<string, object?>? properties);
        IDisposable EnrichScope(params (string Key, object? Value)[] properties);
        void LogInformation(string message);
        void LogInformation(string message, params object?[] args);
        void LogWarning(string message);
        void LogWarning(string message, params object?[] args);
        void LogError(string message);
        void LogError(string message, params object?[] args);
        Task LogTraceAsync(string message, string? action = null, bool requiresResolution = false, HttpContext? context = null);
    }
}
