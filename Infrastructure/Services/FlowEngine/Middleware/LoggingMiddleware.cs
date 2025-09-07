using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Middleware
{
    public class LoggingMiddleware : IFlowMiddleware
    {
        public async Task InvokeAsync(FlowExecutionContext context, Func<Task> next)
        {
            // Logging implementation
            await next();
        }
    }
}
