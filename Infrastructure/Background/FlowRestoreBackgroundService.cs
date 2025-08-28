// Infrastructure/Background/SubscriptionRetryBackgroundService.cs
using Application.Interfaces.Subscription;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Background
{
    public class FlowRestoreBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<FlowRestoreBackgroundService> _logger;
        private bool _isInitialized = false;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

        public FlowRestoreBackgroundService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<FlowRestoreBackgroundService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Flow restore background service is starting");

            using var scope = _serviceScopeFactory.CreateScope();
            var flowEngineService = scope.ServiceProvider.GetRequiredService<IFlowEngineService>();
            var autoResumeService = scope.ServiceProvider.GetRequiredService<IFlowAutoResumeService>();

            if (!_isInitialized)
            {
                try
                {
                    _logger.LogInformation("Checking for incomplete flows");

                    await flowEngineService.RestoreFlowRuntime();
                    await autoResumeService.StartBackgroundCheckingAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error restoring incomplete flows");
                }
                _isInitialized = true;
            }

            /*while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Flow restore background service is watching...");

                    await autoResumeService.CheckAndResumeFlowsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error");
                }

                // Wait for the next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }
            */
            _logger.LogInformation("Flow restore background service is stopping");
        }
    }
}