using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Services.Events
{
    public class FlowEventService : IFlowEventService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IFlowEngineService _flowEngineService;
        private readonly ILogger<FlowEventService> _logger;
        private readonly Dictionary<string, List<Func<object, Task>>> _eventHandlers = new();

        public FlowEventService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _flowEngineService = serviceProvider.GetRequiredService<IFlowEngineService>();
            _logger = serviceProvider.GetRequiredService<ILogger<FlowEventService>>();
        }

        public async Task PublishAsync(string eventType, object eventData, string correlationId = null)
        {
            _logger.LogInformation("Publishing event {EventType} with correlation {CorrelationId}", eventType, correlationId);

            // Process the event for flow resumes
            await ProcessEventAsync(eventType, eventData);

            // Trigger any registered handlers
            if (_eventHandlers.TryGetValue(eventType, out var handlers))
            {
                var tasks = handlers.Select(handler => handler(eventData));
                await Task.WhenAll(tasks);
            }
        }

        public async Task SubscribeAsync(string eventType, Func<object, Task> handler)
        {
            if (!_eventHandlers.ContainsKey(eventType))
            {
                _eventHandlers[eventType] = new List<Func<object, Task>>();
            }
            _eventHandlers[eventType].Add(handler);
        }

        public async Task ProcessEventAsync(string eventType, object eventData)
        {
            var pausedFlows = _flowEngineService.GetPausedFlowsAsync();
            // Check if any paused flows are waiting for this event
            //var persistence = _serviceProvider.GetRequiredService<IFlowPersistence>();
            //var pausedFlows = await persistence.GetPausedFlowsForAutoResumeAsync();

            foreach (var flow in pausedFlows)
            {
                if (flow.ActiveResumeConfig?.EventTriggers?.Any() == true)
                {
                    foreach (var trigger in flow.ActiveResumeConfig.EventTriggers)
                    {
                        if (trigger.EventType == eventType)
                        {
                            bool shouldResume = trigger.EventFilter?.Invoke(eventData) ?? true;

                            if (shouldResume)
                            {
                                _logger.LogInformation("Resuming flow {FlowId} due to event {EventType}", flow.FlowId, eventType);
                                await _flowEngineService.ResumeRuntimeAsync(flow.FlowId);
                               // await persistence.ResumeFlowAsync(flow.FlowId, ResumeReason.Event, "system", $"Event: {eventType}");
                            }
                        }
                    }
                }
            }
        }
    }
}
