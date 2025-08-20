using Infrastructure.Services.FlowEngine.Models;

namespace Infrastructure.Services.FlowEngine.Security
{
    /// <summary>
    /// Flow security service interface with comprehensive security enforcement
    /// </summary>
    public interface IFlowSecurity
    {
        Task<bool> CanStartFlowAsync<TFlow>(string userId, object initialData, CancellationToken cancellationToken);
        Task<bool> CanAccessFlowAsync(string flowId, string userId, CancellationToken cancellationToken);
        Task<bool> CanCancelFlowAsync(string flowId, string userId, CancellationToken cancellationToken);
        Task<bool> CanResumeFlowAsync(string flowId, string userId, CancellationToken cancellationToken);
        Task<FlowQuery> FilterQueryAsync(FlowQuery query, string userId, CancellationToken cancellationToken);
        Task<SignedEvent> SignEventAsync(string eventType, object eventData, string publishedBy, string correlationId, CancellationToken cancellationToken);
        Task<bool> ValidateEventSignatureAsync(SignedEvent signedEvent, CancellationToken cancellationToken);

        // Enhanced security methods for step-level control
        Task<bool> CanResumeFromStepAsync(FlowDefinition flow, string stepName, string userId, CancellationToken cancellationToken);
        Task<bool> CanResumeWithEventAsync(FlowDefinition flow, SignedEvent signedEvent, CancellationToken cancellationToken);
        Task<bool> ValidateEventPayloadForStepAsync(FlowDefinition flow, string stepName, SignedEvent signedEvent, CancellationToken cancellationToken);
        Task<bool> CanExecuteStepAsync(FlowDefinition flow, string stepName, string userId, CancellationToken cancellationToken);
    }
}
