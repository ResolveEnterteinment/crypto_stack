using Infrastructure.Services.FlowEngine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Execution
{
    /// <summary>
    /// Stub flow executor
    /// </summary>
    public sealed class FlowExecutor : IFlowExecutor
    {
        public Task<FlowResult<T>> ExecuteAsync<T>(T flow, CancellationToken cancellationToken) where T : FlowDefinition
        {
            flow.Status = FlowStatus.Completed;
            flow.CompletedAt = DateTime.UtcNow;
            return Task.FromResult(FlowResult<T>.Success(flow, "Executed successfully"));
        }
    }
}
