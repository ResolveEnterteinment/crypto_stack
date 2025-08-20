namespace Infrastructure.Services.FlowEngine.Core.Enums
{
    /// <summary>
    /// Execution strategies for dynamic sub-steps
    /// </summary>
    public enum ExecutionStrategy
    {
        Sequential,      // Execute one after another
        Parallel,        // Execute all simultaneously 
        RoundRobin,      // Distribute across available resources
        Batched,         // Execute in batches with delays
        PriorityBased    // Execute based on priority
    }
}
