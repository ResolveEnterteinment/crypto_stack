namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowEventService
    {
        Task PublishAsync(string eventType, object eventData, string correlationId = null);
        Task SubscribeAsync(string eventType, Func<object, Task> handler);
        Task ProcessEventAsync(string eventType, object eventData);
    }
}
