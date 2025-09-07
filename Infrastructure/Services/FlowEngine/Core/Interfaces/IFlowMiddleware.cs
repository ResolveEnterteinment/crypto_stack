using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowMiddleware
    {
        Task InvokeAsync(FlowExecutionContext context, Func<Task> next);
    }
}
