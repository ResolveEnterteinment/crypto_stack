using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Services.FlowEngine.Engine;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowExecutor
    {
        Task<FlowExecutionResult> ExecuteAsync(Flow flow, CancellationToken cancellationToken);
        Task<FlowExecutionResult> ResumePausedFlowAsync(Flow flow, string reason, CancellationToken cancellationToken);
        Task<FlowExecutionResult> RetryFailedFlowAsync(Flow flow, string reason, CancellationToken cancellationToken);
    }
}
