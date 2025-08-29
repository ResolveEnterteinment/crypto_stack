using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowRuntimeStore
    {
        Dictionary<Guid, FlowDefinition> Flows { get; }
    }
}
