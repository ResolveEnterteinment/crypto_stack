using Infrastructure.Services.FlowEngine.Models;

namespace Infrastructure.Services.FlowEngine.Events
{
    public interface IFlowAuditService
    {
        Task RecordEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken);
        Task<IReadOnlyList<FlowEvent>> GetEventsAsync(string flowId, CancellationToken cancellationToken);
    }
}
