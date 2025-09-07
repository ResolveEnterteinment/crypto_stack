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
        private Dictionary<string, object> _initialData = new();
        private string _userId = "system";
        private string _correlationId = "";
        private Guid? _triggeredBy = null;

        public FlowBuilder(IServiceProvider serviceProvider)
        {
            _serviceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
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

        public FlowBuilder ForUser(string userId)
        {
            _userId = userId ?? "system";
            return this;
        }

        public FlowBuilder WithCorrelation(string correlationId)
        {
            _correlationId = correlationId;
            return this;
        }

        public FlowBuilder TriggeredBy(Guid triggerFlowId)
        {
            _triggeredBy = triggerFlowId;
            return this;
        }

        public Flow Build<TDefinition>() where TDefinition : FlowDefinition
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var flow = Flow.Create<TDefinition>(scope.ServiceProvider, _initialData);
            flow.State.UserId = _userId;
            flow.State.CorrelationId = string.IsNullOrEmpty(_correlationId) ? Guid.NewGuid().ToString() : _correlationId;
            flow.State.TriggeredBy = _triggeredBy;
            return flow;
        }

        public Flow Build(Type definitionType)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var flow = Flow.Create(scope.ServiceProvider, definitionType, _initialData);

            flow.State.UserId = _userId;
            flow.State.CorrelationId = string.IsNullOrEmpty(_correlationId) ? Guid.NewGuid().ToString() : _correlationId;
            flow.State.TriggeredBy = _triggeredBy;
            return flow;
        }
    }
}