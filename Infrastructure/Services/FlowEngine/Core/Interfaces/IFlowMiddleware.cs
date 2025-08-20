using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowMiddleware
    {
        Task InvokeAsync(FlowContext context, Func<Task> next);
    }
}
