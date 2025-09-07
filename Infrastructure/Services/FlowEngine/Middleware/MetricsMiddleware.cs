using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Middleware
{
    public class MetricsMiddleware : IFlowMiddleware
    {
        public async Task InvokeAsync(FlowExecutionContext context, Func<Task> next)
        {
            // Metrics collection implementation
            await next();
        }
    }
}
