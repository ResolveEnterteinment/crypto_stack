using Infrastructure.Services.FlowEngine.Core.Enums;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowResult<T>
    {
        public T Flow { get; set; }
        public FlowStatus Status { get; set; }
        public string Message { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public Exception Error { get; set; }

        public static FlowResult<T> Success(T flow, string message = null) =>
            new() { Flow = flow, Status = FlowStatus.Completed, Message = message };

        public static FlowResult<T> Failure(T flow, string message, Exception error = null) =>
            new() { Flow = flow, Status = FlowStatus.Failed, Message = message, Error = error };
    }
}
