using Infrastructure.Services.FlowEngine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Middleware
{
    public sealed class FlowPerformanceMiddleware : IFlowMiddleware
    {
        public Task InvokeAsync(FlowContext context, Func<CancellationToken, Task> next, CancellationToken cancellationToken)
        {
            return next(cancellationToken);
        }
    }
}
