namespace Infrastructure.Services.FlowEngine.Models
{
    /// <summary>
    /// Enhanced flow status
    /// </summary>
    public enum FlowStatus
    {
        Initializing,
        Ready,
        Running,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Resume reasons
    /// </summary>
    public enum ResumeReason
    {
        Manual,
        Event,
        Condition,
        Timeout,
        System
    }

    public enum PauseReason
    {
        ExternalDependency,
        InsufficientResources,
        ManualApproval,
        DataAvailability,
        SystemMaintenance,
        RateLimitExceeded,
        Custom
    }

    /// <summary>
    /// Flow event types for audit trail
    /// </summary>
    public enum FlowEventType
    {
        Created,
        Started,
        StepStarted,
        StepCompleted,
        StepFailed,
        Paused,
        Resumed,
        Completed,
        Failed,
        Cancelled
    }

    public enum StepExecutionStatus
    {
        Started,
        Completed,
        Failed,
        Skipped
    }

    public enum PersistenceType
    {
        InMemory,
        SqlServer,
        PostgreSQL,
        MongoDB,
        CosmosDB
    }
}
