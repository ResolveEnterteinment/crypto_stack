using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowExecutor
    {
        Task<FlowResult<T>> ExecuteAsync<T>(T flow, CancellationToken cancellationToken) where T : FlowDefinition;
        Task<FlowResult<T>> PauseFlowAsync<T>(T flow, PauseCondition pauseCondition) where T : FlowDefinition;
        Task ResumeFlowAsync<T>(T flow, string reason) where T : FlowDefinition;
    }
}
