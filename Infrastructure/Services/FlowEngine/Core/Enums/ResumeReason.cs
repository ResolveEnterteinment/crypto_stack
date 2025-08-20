namespace Infrastructure.Services.FlowEngine.Core.Enums
{
    /// <summary>
    /// Reason for resuming a flow
    /// </summary>
    public enum ResumeReason
    {
        Manual,
        Event,
        Condition,
        Timeout,
        System
    }
}
