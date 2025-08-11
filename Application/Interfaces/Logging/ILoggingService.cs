using Domain.Constants.Logging;
using Domain.DTOs.Logging;
using Microsoft.AspNetCore.Http;
using System.Runtime.CompilerServices;

namespace Application.Interfaces.Logging
{
    public interface ILoggingService
    {
        IDictionary<string, object?> Context { get; }

        IDisposable BeginScope();
        IDisposable BeginScope(object? state = null);
        IDisposable BeginScope(string? operationName, object? state);
        IDisposable BeginScope(Scope scope);
        IDisposable EnrichScope(params (string Key, object? Value)[] properties);
        IDisposable EnrichScope(IDictionary<string, object?>? properties);

        Task LogTraceAsync(string message, string? action = null, LogLevel? level = LogLevel.Information, bool requiresResolution = false, HttpContext? context = null);

        /// <summary>
        /// Enhanced trace logging with stack trace capture for Error and Critical levels
        /// </summary>
        Task LogTraceWithStackTraceAsync(
            string message,
            Exception? exception = null,
            string? action = null,
            LogLevel? level = LogLevel.Information,
            bool requiresResolution = false,
            HttpContext? context = null,
            [CallerMemberName] string? callerMemberName = null,
            [CallerFilePath] string? callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = 0);

        void LogInformation(string message);
        void LogInformation(string message, params object?[] args);
        void LogWarning(string message);
        void LogWarning(string message, params object?[] args);
        void LogError(string message);
        void LogError(string message, params object?[] args);

        /// <summary>
        /// Enhanced error logging with automatic stack trace capture
        /// </summary>
        void LogError(string message, Exception? exception = null, [CallerMemberName] string? callerMemberName = null, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = 0);

        /// <summary>
        /// Enhanced critical logging with automatic stack trace capture
        /// </summary>
        void LogCritical(string message, Exception? exception = null, [CallerMemberName] string? callerMemberName = null, [CallerFilePath] string? callerFilePath = null, [CallerLineNumber] int callerLineNumber = 0);
    }
}