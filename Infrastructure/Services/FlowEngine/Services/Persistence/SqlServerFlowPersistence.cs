using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Services.Persistence
{
    public class SqlServerFlowPersistence : IFlowPersistence
    {
        public async Task<FlowStatus> GetFlowStatusAsync(string flowId) => throw new NotImplementedException();
        public async Task<bool> CancelFlowAsync(string flowId, string reason) => throw new NotImplementedException();
        public async Task<FlowTimeline> GetFlowTimelineAsync(string flowId) => throw new NotImplementedException();
        public async Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query) => throw new NotImplementedException();
        public async Task<T> LoadFlowAsync<T>(string flowId) where T : FlowDefinition => throw new NotImplementedException();
        public async Task<int> CleanupCompletedFlowsAsync(TimeSpan olderThan) => throw new NotImplementedException();
        public async Task<bool> ResumeFlowAsync(string flowId, ResumeReason reason, string resumedBy, string message = null) => throw new NotImplementedException();
        public async Task<bool> SetResumeConditionAsync(string flowId, ResumeCondition condition) => throw new NotImplementedException();
        public async Task<List<FlowDefinition>> GetPausedFlowsForAutoResumeAsync() => throw new NotImplementedException();
        public async Task SaveFlowStateAsync(FlowDefinition flow) => throw new NotImplementedException();

        public Task<FlowStatus> GetFlowStatusAsync(Guid flowId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CancelFlowAsync(Guid flowId, string reason)
        {
            throw new NotImplementedException();
        }

        public Task<FlowTimeline> GetFlowTimelineAsync(Guid flowId)
        {
            throw new NotImplementedException();
        }

        public Task<T> LoadFlowAsync<T>(Guid flowId) where T : FlowDefinition
        {
            throw new NotImplementedException();
        }

        public Task<bool> ResumeFlowAsync(Guid flowId, ResumeReason reason, string resumedBy, string message = null)
        {
            throw new NotImplementedException();
        }

        public Task<bool> SetResumeConditionAsync(Guid flowId, ResumeCondition condition)
        {
            throw new NotImplementedException();
        }

        public Task<List<FlowDocument>> GetRuntimeFlows()
        {
            throw new NotImplementedException();
        }

        public Task<List<FlowDocument>> GetFlowsByStatusesAsync(FlowStatus[] flowStatuses)
        {
            throw new NotImplementedException();
        }

        public Task<FlowDocument> GetByFlowId(Guid flowId)
        {
            throw new NotImplementedException();
        }
    }
}
