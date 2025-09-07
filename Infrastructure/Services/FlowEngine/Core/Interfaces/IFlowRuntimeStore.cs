using Infrastructure.Services.FlowEngine.Engine;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowRuntimeStore
    {
        Dictionary<Guid, Flow> Flows { get; }
    }
}
