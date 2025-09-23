using Domain.Events.Payment;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Services.Events
{
    public class FlowEventService : IFlowEventService
    {
        private readonly IFlowEngineService _flowEngineService;
        private readonly ILogger<FlowEventService> _logger;
        private readonly Dictionary<string, List<Func<object, Task>>> _eventHandlers = [];

        public FlowEventService(IFlowEngineService flowEngineService, ILogger<FlowEventService> logger)
        {
            _flowEngineService = flowEngineService ?? throw new ArgumentNullException(nameof(flowEngineService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        public void SubscribeAsync(string eventType, Func<object, Task> handler)
        {
            if (!_eventHandlers.ContainsKey(eventType))
            {
                _eventHandlers[eventType] = [];
            }
            _eventHandlers[eventType].Add(handler);
        }

        public async Task ProcessEventAsync(string eventType, object eventData)
        {
            var pausedFlows = _flowEngineService.GetPausedFlows();
            // Check if any paused flows are waiting for this event
            //var persistence = _serviceProvider.GetRequiredService<IFlowPersistence>();
            //var pausedFlows = await persistence.GetPausedFlowsForAutoResumeAsync();

            foreach (var flow in pausedFlows)
            {
                if (flow.Definition.ActiveResumeConfig != null && flow.Definition.ActiveResumeConfig.EventTriggers?.Count != 0)
                {
                    foreach (var trigger in flow.Definition.ActiveResumeConfig.EventTriggers!)
                    {
                        if (trigger.EventType == eventType)
                        {
                            bool shouldResume = trigger.EventFilter?.Invoke(flow.Context, eventData) ?? true;

                            if (shouldResume)
                            {
                                _logger.LogInformation("Resuming flow {FlowId} due to event {EventType}", flow.Id, eventType);
                                await _flowEngineService.ResumeRuntimeAsync(flow.Id, $"Resuming flow {flow.Id} due to event {eventType}");
                            }
                        }
                    }
                }
            }
        }

        public async Task Handle(CheckoutSessionCompletedEvent notification, CancellationToken cancellationToken)
        {
            await PublishAsync("CheckoutSessionCompleted", notification.Session);
        }
    }
}
