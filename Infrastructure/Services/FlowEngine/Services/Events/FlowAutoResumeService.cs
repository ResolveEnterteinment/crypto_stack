using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Services.Events
{
    public class FlowAutoResumeService : IFlowAutoResumeService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FlowAutoResumeService> _logger;
        private Timer _checkTimer;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

        public FlowAutoResumeService(IServiceProvider serviceProvider, ILogger<FlowAutoResumeService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<int> CheckAndResumeFlowsAsync()
        {
            var resumedCount = 0;

            try
            {
                var persistence = _serviceProvider.GetRequiredService<IFlowPersistence>();
                var pausedFlows = await persistence.GetPausedFlowsForAutoResumeAsync();

                _logger.LogDebug("Checking {FlowCount} paused flows for auto-resume conditions", pausedFlows.Count);

                foreach (var flow in pausedFlows)
                {
                    if (await ShouldResumeFlow(flow))
                    {
                        await persistence.ResumeFlowAsync(flow.FlowId, ResumeReason.Condition, "system", "Auto-resume condition met");
                        resumedCount++;
                        _logger.LogInformation("Auto-resumed flow {FlowId}", flow.FlowId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auto-resume check");
            }

            return resumedCount;
        }

        private async Task<bool> ShouldResumeFlow(FlowDefinition flow)
        {
            var resumeConfig = flow.ActiveResumeConfig;
            if (resumeConfig == null) return false;

            // Check timeout condition
            if (resumeConfig.TimeoutDuration.HasValue && flow.PausedAt.HasValue)
            {
                var timeoutAt = flow.PausedAt.Value.Add(resumeConfig.TimeoutDuration.Value);
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
                    var context = new FlowContext
                    {
                        Flow = flow,
                        Services = _serviceProvider
                    };

                    return await resumeConfig.AutoResumeCondition(context);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking auto-resume condition for flow {FlowId}", flow.FlowId);
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
