using Infrastructure.Services.FlowEngine.Core.Interfaces;

namespace Infrastructure.Services.FlowEngine.Engine
{
    public class FlowRuntimeStore : IFlowRuntimeStore
    {
        public Dictionary<Guid, Flow> Flows { get; } = new();
    }
}