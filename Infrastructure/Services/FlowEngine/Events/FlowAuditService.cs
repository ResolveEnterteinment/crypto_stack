using Infrastructure.Services.FlowEngine.Models;

namespace Infrastructure.Services.FlowEngine.Events
{
    public sealed class FlowAuditService : IFlowAuditService
    {
        public Task RecordEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FlowEvent>> GetEventsAsync(string flowId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<FlowEvent>>(Array.Empty<FlowEvent>());
        }
    }
}
