namespace Infrastructure.Services.FlowEngine.Core.Enums
{
    /// <summary>
    /// Reasons why a flow might be paused
    /// </summary>
    public enum PauseReason
    {
        ManualIntervention,
        InsufficientResources,
        ExternalDependencyUnavailable,
        ValidationRequired,
        ApprovalRequired,
        ComplianceReview,
        MaintenanceWindow,
        RateLimitExceeded,
        TemporaryError,
        WaitingForEvent,
        WaitingForPayment,
        Custom
    }
}
