using Application.Interfaces.Exchange;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Infrastructure.HealthChecks
{
    /// <summary>
    /// Health check for exchange API connectivity.
    /// Verifies that at least one configured exchange is accessible and responsive.
    /// </summary>
    public class ExchangeApiHealthCheck : IHealthCheck
    {
        private readonly IExchangeService _exchangeService;
        private readonly ILogger<ExchangeApiHealthCheck> _logger;

        public ExchangeApiHealthCheck(
            IExchangeService exchangeService,
            ILogger<ExchangeApiHealthCheck> logger)
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if any exchanges are configured
                if (!_exchangeService.Exchanges.Any())
                {
                    _logger.LogWarning("No exchanges are configured in the application");
                    return HealthCheckResult.Degraded("No exchanges configured");
                }

                // Test connectivity with all exchanges
                var healthResults = await Task.WhenAll(
                    _exchangeService.Exchanges.Select(async exchange =>
                    {
                        try
                        {
                            var exchangeInstance = exchange.Value;
                            // Check basic balance retrieval as a health indicator
                            var balanceResult = await exchangeInstance.GetBalancesAsync();

                            if (!balanceResult.IsSuccess)
                            {
                                _logger.LogWarning(
                                    "Exchange {ExchangeName} health check failed: {Message}",
                                    exchange.Key, balanceResult.ErrorMessage);

                                return new
                                {
                                    Exchange = exchange.Key,
                                    IsHealthy = false,
                                    Message = balanceResult.ErrorMessage
                                };
                            }

                            return new
                            {
                                Exchange = exchange.Key,
                                IsHealthy = true,
                                Message = "Exchange API is responsive"
                            };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Exception during health check for exchange {ExchangeName}",
                                exchange.Key);

                            return new
                            {
                                Exchange = exchange.Key,
                                IsHealthy = false,
                                Message = $"Exception: {ex.Message}"
                            };
                        }
                    }));

                // Determine overall health status
                var unhealthyExchanges = healthResults.Where(r => !r.IsHealthy).ToList();
                var totalExchanges = _exchangeService.Exchanges.Count;
                var healthyExchanges = totalExchanges - unhealthyExchanges.Count;

                // Create detailed status data - using proper IReadOnlyDictionary<string, object>
                var data = new Dictionary<string, object>();
                foreach (var result in healthResults)
                {
                    data.Add($"exchange_{result.Exchange}", result.Message);
                }

                // If all exchanges are down, report unhealthy
                if (healthyExchanges == 0)
                {
                    return HealthCheckResult.Unhealthy(
                        "All exchanges are unreachable",
                        data: data);
                }

                // If some exchanges are down, report degraded
                if (unhealthyExchanges.Any())
                {
                    var description = $"{healthyExchanges}/{totalExchanges} exchanges are operational";
                    return HealthCheckResult.Degraded(description, data: data);
                }

                // All exchanges are operational
                return HealthCheckResult.Healthy(
                    "All exchanges are operational",
                    data: data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exchange API health check failed with unhandled exception");
                return HealthCheckResult.Unhealthy(
                    "Health check failed with exception",
                    exception: ex);
            }
        }
    }
}