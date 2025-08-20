using Infrastructure.Services.FlowEngine.Models;

namespace Infrastructure.Services.FlowEngine.Execution
{
    public interface IFlowExecutor
    {
        Task<FlowResult<T>> ExecuteAsync<T>(T flow, CancellationToken cancellationToken) where T : FlowDefinition;
    }
}
