using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Services.Recovery
{
    public class FlowRecoveryService : IFlowRecovery
    {
        private readonly IFlowPersistence _persistence;
        private readonly ILogger<FlowRecoveryService> _logger;

        public FlowRecoveryService(IFlowPersistence persistence, ILogger<FlowRecoveryService> logger)
        {
            _persistence = persistence;
            _logger = logger;
        }

        public async Task<RecoveryResult> RecoverAllAsync()
        {
            var startTime = DateTime.UtcNow;
            var result = new RecoveryResult();
            
            try
            {
                // Get all flows in Running state that might have crashed
                var query = new FlowQuery 
                { 
                    Status = FlowStatus.Running,
                    CreatedBefore = DateTime.UtcNow.AddMinutes(-30) // Consider flows running > 30min as potentially crashed
                };
                
                var potentiallyCrashedFlows = await _persistence.QueryFlowsAsync(query);
                result.TotalFlowsChecked = potentiallyCrashedFlows.TotalCount;

                foreach (var flowSummary in potentiallyCrashedFlows.Items)
                {
                    try
                    {
                        // Attempt to recover the flow
                        await _persistence.ResumeFlowAsync(flowSummary.FlowId, ResumeReason.System, "system", "Auto-recovery");
                        result.RecoveredFlowIds.Add(flowSummary.FlowId);
                        result.FlowsRecovered++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to recover flow {FlowId}", flowSummary.FlowId);
                        result.FailedFlowsDict.Add(flowSummary.FlowId, ex);
                        result.FlowsFailed++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recovery operation failed");
                throw;
            }
            finally
            {
                result.Duration = DateTime.UtcNow - startTime;
                result.CompletedAt = DateTime.UtcNow;
            }

            return result;
        }
    }
}
