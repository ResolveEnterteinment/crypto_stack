using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowNotificationService
    {
        Task NotifyFlowStatusChanged(FlowDefinition flow);
        Task NotifyStepStatusChanged(FlowDefinition flow, FlowStep step);
        Task NotifyFlowError(Guid flowId, string error);
    }
}
