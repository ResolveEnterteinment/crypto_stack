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
        Task<FlowResult<TFlow>> StartAsync<TFlow>(Dictionary<string, object>? initialData = null, string userId = null, string correlationId = null, CancellationToken cancellationToken = default) where TFlow : FlowDefinition;
        Task<FlowDefinition?> GetFlowById(Guid flowId);
        FlowStatus? GetStatus(Guid flowId);
        Task<FlowResult<FlowDefinition>> ResumeRuntimeAsync(Guid flowId, CancellationToken cancellationToken = default);
        Task RestoreFlowRuntime();
        Task FireAsync<TFlow>(Dictionary<string, object>? initialData = null, string userId = null) where TFlow : FlowDefinition;
        Task<FlowResult<TTriggered>> TriggerAsync<TTriggered>(FlowContext context, Dictionary<string, object>? triggerData = null) where TTriggered : FlowDefinition;
        Task<bool> CancelAsync(Guid flowId, string reason = null);
        Task<FlowTimeline> GetTimelineAsync(Guid flowId);
        Task<PagedResult<FlowSummary>> QueryAsync(FlowQuery query);
        Task<RecoveryResult> RecoverCrashedFlowsAsync();
        Task<int> CleanupAsync(TimeSpan olderThan);

        // NEW: Pause/Resume capabilities
        Task<bool> ResumeManuallyAsync(Guid flowId, string userId, string reason = null);
        Task<bool> ResumeByEventAsync(Guid flowId, string eventType, object eventData = null);
        List<FlowDefinition> GetPausedFlowsAsync();
        Task<PagedResult<FlowSummary>> GetPausedFlowSummariesAsync(FlowQuery query = null);
        Task<bool> SetResumeConditionAsync(Guid flowId, ResumeCondition condition);
        Task PublishEventAsync(string eventType, object eventData, string correlationId = null);
    }
}
