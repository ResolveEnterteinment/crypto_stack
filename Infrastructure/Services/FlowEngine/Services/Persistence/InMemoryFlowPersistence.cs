using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Services.FlowEngine.Services.Persistence
{
    public class InMemoryFlowPersistence : IFlowPersistence
    {
        public async Task<FlowStatus> GetFlowStatusAsync(Guid flowId) => throw new NotImplementedException();
        public async Task<bool> CancelFlowAsync(Guid flowId, string reason) => throw new NotImplementedException();
        public async Task<FlowTimeline> GetFlowTimelineAsync(Guid flowId) => throw new NotImplementedException();
        public async Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query) => throw new NotImplementedException();
        public async Task<T> LoadFlowAsync<T>(Guid flowId) where T : FlowDefinition => throw new NotImplementedException();
        public async Task<int> CleanupCompletedFlowsAsync(TimeSpan olderThan) => throw new NotImplementedException();
        public async Task<bool> ResumeFlowAsync(Guid flowId, ResumeReason reason, string resumedBy, string message = null) => throw new NotImplementedException();
        public async Task<bool> SetResumeConditionAsync(Guid flowId, ResumeCondition condition) => throw new NotImplementedException();
        public async Task<List<FlowDefinition>> GetPausedFlowsForAutoResumeAsync() => throw new NotImplementedException();
        public async Task SaveFlowStateAsync(FlowDefinition flow) => throw new NotImplementedException();

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
