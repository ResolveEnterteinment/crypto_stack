namespace Infrastructure.Services.FlowEngine.Core.Enums
{
    public enum FlowStatus
    {
        Initializing,
        Ready,
        Running,
        Paused,          // NEW: Flow is paused waiting for condition/event/manual intervention
        Completed,
        Failed,
        Cancelled
    }
}
