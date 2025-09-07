using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Engine;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowNotificationService
    {
        Task NotifyFlowStatusChanged(Flow flow);
        Task NotifyStepStatusChanged(Flow flow, FlowStep step);
        Task NotifyFlowError(Guid flowId, string error);
    }
}
