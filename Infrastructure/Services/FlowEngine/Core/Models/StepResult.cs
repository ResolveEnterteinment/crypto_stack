using Infrastructure.Utilities;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class StepResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public Dictionary<string, SafeObject> Data { get; set; } = [];

        public static StepResult Success(string message = null, Dictionary<string, object> data = null) =>
            new() { IsSuccess = true, Message = message, Data = data.ToSafe() ?? [] };

        public static StepResult Failure(string message) =>
            new() { IsSuccess = false, Message = message };

        public static StepResult NotFound(string name, string? id = null)
        {
             var message = id is null ? $"{name} not found" : $"{name} with ID {id} not found";
            return new StepResult { IsSuccess = false, Message = message };
        }

        public static StepResult NotAuthorized(string? message = null)
        {
            return new StepResult { IsSuccess = false, Message = message ?? "Unauthorized access" };
        }

        public static StepResult ConcurrencyConflict(string? message = null, Dictionary<string, object> data = null)
        {
            return new StepResult { IsSuccess = true, Message = message ?? "Idempotent request. Returning cached result.", Data = data.ToSafe() ?? [] };
        }
    }
}
