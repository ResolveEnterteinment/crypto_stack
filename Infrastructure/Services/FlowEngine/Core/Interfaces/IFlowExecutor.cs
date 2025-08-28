using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowExecutor
    {
        Task<FlowResult<T>> ExecuteAsync<T>(T flow, CancellationToken cancellationToken) where T : FlowDefinition;
        Task ResumeFlowAsync<T>(T flow, string reason) where T : FlowDefinition;
    }
}
