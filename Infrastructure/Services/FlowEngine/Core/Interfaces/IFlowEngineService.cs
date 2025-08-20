using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    /// <summary>
    /// Injectable Flow Engine Service - Use this for dependency injection scenarios
    /// </summary>
    public interface IFlowEngineService
    {
        Task<FlowResult<TFlow>> StartAsync<TFlow>(object initialData = null, string userId = null, string correlationId = null, CancellationToken cancellationToken = default) where TFlow : FlowDefinition, new();
        Task<FlowResult<TFlow>> ResumeAsync<TFlow>(string flowId, CancellationToken cancellationToken = default) where TFlow : FlowDefinition, new();
        Task FireAsync<TFlow>(object initialData = null, string userId = null) where TFlow : FlowDefinition, new();
        Task<FlowResult<TTriggered>> TriggerAsync<TTriggered>(FlowContext context, object triggerData = null) where TTriggered : FlowDefinition, new();
        Task<FlowStatus> GetStatusAsync(string flowId);
        Task<bool> CancelAsync(string flowId, string reason = null);
        Task<FlowTimeline> GetTimelineAsync(string flowId);
        Task<PagedResult<FlowSummary>> QueryAsync(FlowQuery query);
        Task<RecoveryResult> RecoverCrashedFlowsAsync();
        Task<int> CleanupAsync(TimeSpan olderThan);

        // NEW: Pause/Resume capabilities
        Task<bool> ResumeManuallyAsync(string flowId, string userId, string reason = null);
        Task<bool> ResumeByEventAsync(string flowId, string eventType, object eventData = null);
        Task<PagedResult<FlowSummary>> GetPausedFlowsAsync(FlowQuery query = null);
        Task<bool> SetResumeConditionAsync(string flowId, ResumeCondition condition);
        Task PublishEventAsync(string eventType, object eventData, string correlationId = null);
        Task<int> CheckAutoResumeConditionsAsync();
    }
}
