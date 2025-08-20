using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Middleware
{
    public class TimeoutMiddleware : IFlowMiddleware
    {
        public async Task InvokeAsync(FlowContext context, Func<Task> next)
        {
            // Timeout handling implementation
            await next();
        }
    }
}
