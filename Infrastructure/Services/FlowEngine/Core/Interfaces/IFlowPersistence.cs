using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowPersistence
    {
        Task<FlowStatus> GetFlowStatusAsync(string flowId);
        Task<bool> CancelFlowAsync(string flowId, string reason);
        Task<FlowTimeline> GetFlowTimelineAsync(string flowId);
        Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query);
        Task<T> LoadFlowAsync<T>(string flowId) where T : FlowDefinition;
        Task<int> CleanupCompletedFlowsAsync(TimeSpan olderThan);

        // NEW: Pause/Resume persistence methods
        Task<bool> ResumeFlowAsync(string flowId, ResumeReason reason, string resumedBy, string message = null);
        Task<bool> SetResumeConditionAsync(string flowId, ResumeCondition condition);
        Task<List<FlowDefinition>> GetPausedFlowsForAutoResumeAsync();
        Task SaveFlowStateAsync(FlowDefinition flow);
    }
}
