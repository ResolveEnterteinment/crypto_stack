using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Engine;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Services.PauseResume
{
    public class FlowAutoResumeService : IFlowAutoResumeService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FlowAutoResumeService> _logger;
        private readonly IFlowEngineService _flowEngineService;
        private readonly IFlowExecutor _flowExecutor;

        private Timer _checkTimer;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

        public FlowAutoResumeService(
            IServiceProvider serviceProvider, 
            ILogger<FlowAutoResumeService> logger,
            IFlowEngineService flowEngineService,
            IFlowExecutor flowExecutor)
        {
            _logger = logger;
            _flowEngineService = flowEngineService;
            _flowExecutor = flowExecutor;
            _serviceProvider = serviceProvider;
        }

        public async Task<int> CheckAndResumeFlowsAsync()
        {
            var resumedCount = 0;
            _logger.LogDebug("Checking paused flows for auto-resume conditions");
            try
            {
                var pausedFlows = _flowEngineService.GetPausedFlows();

                _logger.LogDebug("Checking {FlowCount} paused flows for auto-resume conditions", pausedFlows.Count);

                foreach (var flow in pausedFlows)
                {
                    if (await ShouldResumeFlow(flow))
                    {
                        try
                        {
                            await flow.ResumeAsync("Auto-resume condition met");
                            resumedCount++;
                            _logger.LogInformation("Auto-resumed flow {FlowId}", flow.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to resume flow");
                        }
                        
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auto-resume check");
            }

            return resumedCount;
        }

        private async Task<bool> ShouldResumeFlow(Flow flow)
        {
            var resumeConfig = flow.Definition.ActiveResumeConfig; // ActiveResumeConfig returns null! 
            if (resumeConfig == null) return false;

            // Check timeout condition
            if (resumeConfig.TimeoutDuration.HasValue && flow.State.PausedAt.HasValue)
            {
                var timeoutAt = flow.State.PausedAt.Value.Add(resumeConfig.TimeoutDuration.Value);
                if (DateTime.UtcNow >= timeoutAt)
                {
                    return resumeConfig.ResumeOnTimeout;
                }
            }

            // Check auto-resume condition
            if (resumeConfig.AutoResumeCondition != null)
            {
                try
                {
                    var context = new FlowExecutionContext
                    {
                        Flow = flow,
                        CurrentStep = flow.Definition.Steps[flow.State.CurrentStepIndex],
                        Services = _serviceProvider
                    };

                    return await resumeConfig.AutoResumeCondition(context);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking auto-resume condition for flow {FlowId}", flow.Id);
                    return false;
                }
            }

            return false;
        }

        public async Task StartBackgroundCheckingAsync()
        {
            _checkTimer = new Timer(async _ => await CheckAndResumeFlowsAsync(), null, TimeSpan.Zero, _checkInterval);
            _logger.LogInformation("Started background auto-resume checking with interval {Interval}", _checkInterval);
        }

        public async Task StopBackgroundCheckingAsync()
        {
            _checkTimer?.Dispose();
            _checkTimer = null;
            _logger.LogInformation("Stopped background auto-resume checking");
        }
    }
}
