using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.PauseResume
{
    /// <summary>
    /// Event trigger for resuming flows
    /// </summary>
    public class EventTrigger
    {
        public string EventType { get; set; }
        public Func<FlowExecutionContext, object, bool> EventFilter { get; set; }
    }
}
