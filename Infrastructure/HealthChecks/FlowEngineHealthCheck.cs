using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Engine;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Infrastructure.HealthChecks
{
    /// <summary>
    /// Health check for FlowEngine service.
    /// Monitors flow execution statistics, running flows, and system health.
    /// </summary>
    public class FlowEngineHealthCheck : IHealthCheck
    {
        private readonly IFlowEngineService _flowEngineService;
        private readonly ILogger<FlowEngineHealthCheck> _logger;

        public FlowEngineHealthCheck(
            IFlowEngineService flowEngineService,
            ILogger<FlowEngineHealthCheck> logger)
        {
            _flowEngineService = flowEngineService ?? throw new ArgumentNullException(nameof(flowEngineService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get FlowEngine health information using the extension method
                var flowHealth = await _flowEngineService.GetHealth();
                var flowStats = await _flowEngineService.GetStatistics(TimeSpan.FromHours(24));

                // Create detailed status data
                var data = new Dictionary<string, object>
                {
                    ["running_flows"] = flowHealth.RunningFlowsCount,
                    ["paused_flows"] = flowHealth.PausedFlowsCount,
                    ["recent_failures"] = flowHealth.RecentFailuresCount,
                    ["total_flows_24h"] = flowStats.TotalFlows,
                    ["completed_flows_24h"] = flowStats.CompletedFlows,
                    ["failed_flows_24h"] = flowStats.FailedFlows,
                    ["success_rate_24h"] = flowStats.SuccessRate,
                    ["checked_at"] = flowHealth.CheckedAt,
                    ["flow_engine_status"] = flowHealth.Status
                };

                // Determine health status based on FlowEngine metrics
                if (!flowHealth.IsHealthy)
                {
                    var reason = $"FlowEngine is in degraded state. Recent failures: {flowHealth.RecentFailuresCount}";
                    _logger.LogWarning("FlowEngine health check failed: {Reason}", reason);
                    return HealthCheckResult.Degraded(reason, data: data);
                }

                // Check for high failure rates in the last 24 hours
                if (flowStats.TotalFlows > 0 && flowStats.SuccessRate < 80.0)
                {
                    var reason = $"Low success rate in last 24h: {flowStats.SuccessRate:F1}%";
                    _logger.LogWarning("FlowEngine success rate is concerning: {SuccessRate}%", flowStats.SuccessRate);
                    return HealthCheckResult.Degraded(reason, data: data);
                }

                // Check for excessive failed flows
                if (flowStats.FailedFlows > 50)
                {
                    var reason = $"High number of failed flows in last 24h: {flowStats.FailedFlows}";
                    _logger.LogWarning("FlowEngine has high failure count: {FailedFlows}", flowStats.FailedFlows);
                    return HealthCheckResult.Degraded(reason, data: data);
                }

                // Check for too many long-running paused flows (potential stuck flows)
                if (flowHealth.PausedFlowsCount > 100)
                {
                    var reason = $"High number of paused flows: {flowHealth.PausedFlowsCount}";
                    _logger.LogWarning("FlowEngine has many paused flows: {PausedFlows}", flowHealth.PausedFlowsCount);
                    return HealthCheckResult.Degraded(reason, data: data);
                }

                // FlowEngine is healthy
                var successMessage = $"FlowEngine is healthy. Running: {flowHealth.RunningFlowsCount}, Success rate: {flowStats.SuccessRate:F1}%";
                return HealthCheckResult.Healthy(successMessage, data: data);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("FlowEngine not initialized"))
            {
                _logger.LogError(ex, "FlowEngine is not initialized during health check");
                return HealthCheckResult.Unhealthy(
                    "FlowEngine is not initialized",
                    exception: ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FlowEngine health check failed with unhandled exception");
                return HealthCheckResult.Unhealthy(
                    "FlowEngine health check failed with exception",
                    exception: ex);
            }
        }
    }
}