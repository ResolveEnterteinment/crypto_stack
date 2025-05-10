// Infrastructure/Background/SubscriptionRetryBackgroundService.cs
using Application.Interfaces.Subscription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Background
{
    public class SubscriptionRetryBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<SubscriptionRetryBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

        public SubscriptionRetryBackgroundService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<SubscriptionRetryBackgroundService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Subscription retry background service is starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingRetries(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing pending subscription retries");
                }

                // Wait for the next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Subscription retry background service is stopping");
        }

        private async Task ProcessPendingRetries(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Checking for pending subscription payment retries");

            using var scope = _serviceScopeFactory.CreateScope();
            var retryService = scope.ServiceProvider.GetRequiredService<ISubscriptionRetryService>();

            await retryService.ProcessPendingRetriesAsync();
        }
    }
}