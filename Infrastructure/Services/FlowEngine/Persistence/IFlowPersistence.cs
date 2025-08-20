using Infrastructure.Services.FlowEngine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Persistence
{
    public interface IFlowPersistence
    {
        Task<FlowStatus> GetFlowStatusAsync(string flowId, CancellationToken cancellationToken);
        Task<bool> CancelFlowAsync(string flowId, string reason, CancellationToken cancellationToken);
        Task<FlowTimeline> GetFlowTimelineAsync(string flowId, CancellationToken cancellationToken);
        Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query, CancellationToken cancellationToken);
        Task<T?> LoadFlowAsync<T>(string flowId, CancellationToken cancellationToken) where T : FlowDefinition;
        Task SaveFlowAsync(FlowDefinition flow, CancellationToken cancellationToken);
        Task<bool> ResumeFlowAsync(string flowId, ResumeReason reason, string resumedBy, string message, CancellationToken cancellationToken);
        Task<IReadOnlyList<FlowDefinition>> GetPausedFlowsForAutoResumeAsync(CancellationToken cancellationToken);
        Task SaveEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken);
        Task<IReadOnlyList<FlowEvent>> GetEventsAsync(string flowId, CancellationToken cancellationToken);
    }
}
