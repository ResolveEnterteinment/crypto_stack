using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Services.FlowEngine.Engine;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    /// <summary>
    /// Injectable Flow Engine Service - Use this for dependency injection scenarios
    /// </summary>
    public interface IFlowEngineService
    {
        Task<FlowExecutionResult> StartAsync<TFlow>(
            Dictionary<string, object>? initialData = null,
            string? userId = null,
            string? userEmail = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition;

        void AddFlowToRuntimeStore(Flow flow);

        Task<Flow?> GetFlowById(Guid flowId);
        FlowStatus? GetStatus(Guid flowId);
        Task<FlowExecutionResult> ResumeRuntimeAsync(
            Guid flowId,
            string reason,
            CancellationToken cancellationToken = default);

        Task RestoreFlowRuntime();
        Task FireAsync<TFlow>(
            Dictionary<string, object>? initialData = null,
            string? userId = null)
            where TFlow : FlowDefinition;
        Task<bool> CancelAsync(Guid flowId, string reason = null);
        Task<FlowTimeline> GetTimelineAsync(Guid flowId);
        Task<PagedResult<FlowSummary>> QueryAsync(FlowQuery query);
        Task<RecoveryResult> RecoverCrashedFlowsAsync();
        Task<int> CleanupAsync(TimeSpan olderThan);

        // NEW: Pause/Resume capabilities
        Task<bool> ResumeManuallyAsync(Guid flowId, string userId, string reason = null);
        Task<bool> ResumeByEventAsync(Guid flowId, string eventType, object eventData = null);
        List<Flow> GetPausedFlows();
        List<Flow> GetPausedFlows<FlowType>();
        Task PublishEventAsync(string eventType, object eventData, string correlationId = null);

        // NEW: Health and Statistics methods for health checks
        Task<FlowEngineHealth> GetHealth();
        Task<FlowEngineStatistics> GetStatistics(TimeSpan timeWindow);
    }
}