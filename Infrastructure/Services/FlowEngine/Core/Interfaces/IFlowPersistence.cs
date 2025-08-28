using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Services.FlowEngine.Services.Persistence;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowPersistence
    {
        Task<FlowDocument> GetByFlowId(Guid flowId);
        Task<FlowStatus> GetFlowStatusAsync(Guid flowId);
        Task<bool> CancelFlowAsync(Guid flowId, string reason);
        Task<FlowTimeline> GetFlowTimelineAsync(Guid flowId);
        Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query);
        Task<int> CleanupCompletedFlowsAsync(TimeSpan olderThan);

        // NEW: Pause/Resume persistence methods
        Task<bool> ResumeFlowAsync(Guid flowId, ResumeReason reason, string resumedBy, string message = null);
        Task<bool> SetResumeConditionAsync(Guid flowId, ResumeCondition condition);
        Task<List<FlowDocument>> GetRuntimeFlows();
        Task<List<FlowDocument>> GetFlowsByStatusesAsync(FlowStatus[] flowStatuses);
        Task SaveFlowStateAsync(FlowDefinition flow);
    }
}
