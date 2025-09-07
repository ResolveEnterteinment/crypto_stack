using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Services.FlowEngine.Engine;
using Infrastructure.Services.FlowEngine.Services.Persistence;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowPersistence
    {
        Task<FlowState> GetByFlowId(Guid flowId);
        Task<FlowTimeline> GetFlowTimelineAsync(Guid flowId);
        Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query);
        Task<int> CleanupCompletedFlowsAsync(TimeSpan olderThan);

        // NEW: Pause/Resume persistence methods
        Task<bool> SetResumeConditionAsync(Guid flowId, ResumeCondition condition);
        Task<List<FlowState>> GetFlowsByStatusesAsync(FlowStatus[] flowStatuses);
        Task SaveFlowStateAsync(FlowState flow);
    }
}
