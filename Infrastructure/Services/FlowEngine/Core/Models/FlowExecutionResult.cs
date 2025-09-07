using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Engine;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowExecutionResult
    {
        public Guid FlowId { get; set; }
        public FlowStatus Status { get; set; }
        public string Message { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public Exception? Error { get; set; } = null;

        public static FlowExecutionResult Success(Guid flowId, string message = null) =>
            new() { 
                FlowId = flowId, 
                Status = FlowStatus.Completed, 
                Message = message  ?? $"Flow completed successfully."
            };

        public static FlowExecutionResult Failure(Guid flowId, string message, Exception error = null) =>
            new() { 
                FlowId = flowId,
                Status = FlowStatus.Failed, 
                Message = message ?? $"Flow failed with error: {error.Message}",
                Error = error 
            };
        public static FlowExecutionResult Cancelled(Guid flowId, string message = null) =>
            new() { 
                FlowId = flowId, 
                Status = FlowStatus.Cancelled, 
                Message = message ?? $"Flow was cancelled."
            };

        public static FlowExecutionResult Paused(Guid flowId, string message = null) =>
            new() { 
                FlowId = flowId, 
                Status = FlowStatus.Paused, 
                Message = message ?? $"Flow is paused and awaiting resumption."
            };

        public static FlowExecutionResult Resumed(Guid flowId, string message = null) =>
            new() { 
                FlowId = flowId, 
                Status = FlowStatus.Running, 
                Message = message ?? $"Flow has resumed execution."
            };
    }
}
