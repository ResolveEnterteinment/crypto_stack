using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.FlowEngine.Core.Builders
{
    /// <summary>
    /// Fluent builder for creating flows
    /// </summary>
    public class FlowBuilder
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly CancellationToken? _cancellationToken = null;
        private Dictionary<string, object> _initialData = new();
        private string _userId = "system";
        private string? _userEmail = null;
        private string _correlationId = "";
        private TriggeredFlowData? _triggeredBy = null;

        public FlowBuilder(IServiceProvider serviceProvider, CancellationToken? cancellationToken = null)
        {
            _serviceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            _cancellationToken = cancellationToken;
        }

        public FlowBuilder WithData(string key, object value)
        {
            _initialData[key] = value;
            return this;
        }

        public FlowBuilder WithData(Dictionary<string, object> data)
        {
            if (data != null)
            {
                foreach (var kvp in data)
                    _initialData[kvp.Key] = kvp.Value;
            }
            return this;
        }

        public FlowBuilder ForUser(string userId, string? email = null)
        {
            {
                _userId = userId ?? "system";
                _userEmail = email;
                return this;
            }
        }

        public FlowBuilder WithCorrelation(string correlationId)
        {
            _correlationId = correlationId;
            return this;
        }

        public FlowBuilder TriggeredBy(TriggeredFlowData triggerData)
        {
            _triggeredBy = triggerData;
            return this;
        }

        public Flow Build<TDefinition>() where TDefinition : FlowDefinition
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var flow = Flow.Create<TDefinition>(scope.ServiceProvider, _initialData);
            flow.State.UserId = _userId;
            flow.State.UserEmail = _userEmail;
            flow.State.CorrelationId = string.IsNullOrEmpty(_correlationId) ? Guid.NewGuid().ToString() : _correlationId;
            flow.State.TriggeredBy = _triggeredBy;

            // Add this line to match the non-generic Build method:
            var flowEngineService = flow.GetService<IFlowEngineService>();
            flowEngineService.AddFlowToRuntimeStore(flow);

            return flow;
        }

        public Flow Build(Type definitionType)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var flow = Flow.Create(scope.ServiceProvider, definitionType, _initialData, _cancellationToken);

            flow.State.UserId = _userId;
            flow.State.UserEmail = _userEmail;
            flow.State.CorrelationId = string.IsNullOrEmpty(_correlationId) ? Guid.NewGuid().ToString() : _correlationId;
            flow.State.TriggeredBy = _triggeredBy;

            var flowEngineService = flow.GetService<IFlowEngineService>();
            flowEngineService.AddFlowToRuntimeStore(flow);

            return flow;
        }
    }
}