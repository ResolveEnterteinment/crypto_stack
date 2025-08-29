using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Engine
{
    public class FlowRuntimeStore : IFlowRuntimeStore
    {
        public Dictionary<Guid, FlowDefinition> Flows { get; } = new();
    }
}