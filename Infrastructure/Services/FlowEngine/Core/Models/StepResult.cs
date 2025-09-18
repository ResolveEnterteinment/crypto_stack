using Infrastructure.Utilities;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class StepResult
    {
        public bool IsSuccess { get; set; } = false;
        public string Message { get; set; } = string.Empty;
        public SafeObject? Data { get; set; } = null;

        public static StepResult Success(string? message = null, object? data = null) =>
            new() { IsSuccess = true, Message = message, Data = SafeObject.FromValue(data)};

        public static StepResult Paused(string? message = null, object? data = null) =>
            new() { IsSuccess = true, Message = message ?? "Step is paused", Data = SafeObject.FromValue(data) };

        public static StepResult Failure(string message, Exception? ex = null) =>
            new() { IsSuccess = false, Message = message, Data = SafeObject.FromValue(ex) };

        public static StepResult Cancel(string? message = null) =>
            new() { IsSuccess = true, Message = message ?? "Step execution will be cancelled." };
        public static StepResult NotFound(string name, string? id = null)
        {
             var message = id is null ? $"{name} not found" : $"{name} with ID {id} not found";
            return new StepResult { IsSuccess = false, Message = message };
        }

        public static StepResult NotAuthorized(string? message = null)
        {
            return new StepResult { IsSuccess = false, Message = message ?? "Unauthorized access" };
        }

        public static StepResult ConcurrencyConflict(string? message = null, object data = null)
        {
            return new StepResult { IsSuccess = true, Message = message ?? "Idempotent request. Returning cached result.", Data = SafeObject.FromValue(data) };
        }
    }
}
