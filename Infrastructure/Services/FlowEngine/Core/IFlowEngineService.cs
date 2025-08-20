using Infrastructure.Services.FlowEngine.Models;
using Infrastructure.Services.FlowEngine.Validation;

namespace Infrastructure.Services.FlowEngine.Core
{
    /// <summary>
    /// Primary Flow Engine Service Interface - Use this for dependency injection
    /// This is the authoritative API - the static facade forwards to this service
    /// </summary>
    public interface IFlowEngineService
    {
        /// <summary>
        /// Start a new flow with typed, validated initial data
        /// </summary>
        Task<FlowResult<TFlow>> StartAsync<TFlow, TInit>(
            TInit initialData,
            string userId = null,
            string correlationId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
            where TInit : class, IValidatable;

        /// <summary>
        /// Resume a previously paused flow
        /// </summary>
        Task<FlowResult<TFlow>> ResumeAsync<TFlow>(
            string flowId,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new();

        /// <summary>
        /// Fire-and-forget flow execution with proper error handling
        /// </summary>
        Task FireAsync<TFlow, TInit>(
            TInit initialData,
            string userId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
            where TInit : class, IValidatable;

        /// <summary>
        /// Trigger a child flow from a parent flow context
        /// </summary>
        Task<FlowResult<TTriggered>> TriggerAsync<TTriggered, TTriggerData>(
            FlowContext context,
            TTriggerData triggerData,
            CancellationToken cancellationToken = default)
            where TTriggered : FlowDefinition, new()
            where TTriggerData : class, IValidatable;

        /// <summary>
        /// Get comprehensive flow status with security checks
        /// </summary>
        Task<FlowStatus> GetStatusAsync(string flowId, string requestingUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cancel a flow with proper authorization
        /// </summary>
        Task<bool> CancelAsync(string flowId, string userId, string reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get complete flow execution timeline
        /// </summary>
        Task<FlowTimeline> GetTimelineAsync(string flowId, string requestingUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Query flows with security filtering
        /// </summary>
        Task<PagedResult<FlowSummary>> QueryAsync(FlowQuery query, string requestingUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Manual flow resume with security and validation
        /// </summary>
        Task<bool> ResumeManuallyAsync(string flowId, string userId, string reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Event-based flow resume with signature validation
        /// </summary>
        Task<bool> ResumeByEventAsync(string flowId, SignedEvent signedEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publish signed event that may resume flows
        /// </summary>
        Task PublishEventAsync(string eventType, object eventData, string publishedBy, string correlationId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get paused flows with role-based access
        /// </summary>
        Task<PagedResult<FlowSummary>> GetPausedFlowsAsync(FlowQuery query, string requestingUserId, CancellationToken cancellationToken = default);
    }
}
