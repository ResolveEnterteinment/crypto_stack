using Infrastructure.Services.FlowEngine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Persistence
{
    public sealed class SqlServerFlowPersistence : IFlowPersistence
    {
        public Task<FlowStatus> GetFlowStatusAsync(string flowId, CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task<bool> CancelFlowAsync(string flowId, string reason, CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task<FlowTimeline> GetFlowTimelineAsync(string flowId, CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query, CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task<T?> LoadFlowAsync<T>(string flowId, CancellationToken cancellationToken) where T : FlowDefinition =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task SaveFlowAsync(FlowDefinition flow, CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task<bool> ResumeFlowAsync(string flowId, ResumeReason reason, string resumedBy, string message, CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task<IReadOnlyList<FlowDefinition>> GetPausedFlowsForAutoResumeAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task SaveEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");

        public Task<IReadOnlyList<FlowEvent>> GetEventsAsync(string flowId, CancellationToken cancellationToken) =>
            throw new NotImplementedException("SQL Server persistence not implemented");
    }
}
