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
    public class InMemoryFlowPersistence : IFlowPersistence
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
    }
}
