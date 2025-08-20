namespace Infrastructure.Services.FlowEngine.Models
{
    /// <summary>
    /// Enhanced flow result with better error handling
    /// </summary>
    public sealed record FlowResult<T>
    {
        public T? Flow { get; init; }
        public FlowStatus Status { get; init; }
        public string Message { get; init; } = string.Empty;
        public TimeSpan ExecutionTime { get; init; }
        public Exception? Error { get; init; }
        public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
        public bool IsSuccess => Status is FlowStatus.Completed;

        public static FlowResult<T> Success(T flow, string message = "") =>
            new() { Flow = flow, Status = FlowStatus.Completed, Message = message };

        public static FlowResult<T> Failure(T? flow, string message, Exception? error = null) =>
            new() { Flow = flow, Status = FlowStatus.Failed, Message = message, Error = error };

        public static FlowResult<T> ValidationFailure(IReadOnlyList<string> errors) =>
            new() { Status = FlowStatus.Failed, Message = "Validation failed", ValidationErrors = errors };

        public static FlowResult<T> Unauthorized(string message) =>
            new() { Status = FlowStatus.Failed, Message = message };
    }
}
