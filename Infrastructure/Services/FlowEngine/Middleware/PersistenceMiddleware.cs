using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Middleware
{
    public class PersistenceMiddleware : IFlowMiddleware
    {
        public async Task InvokeAsync(FlowExecutionContext context, Func<Task> next)
        {
            // Persistence implementation
            await next();
        }
    }
}
