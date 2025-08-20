using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.BackgroundServices
{
    /// <summary>
    /// Background service to process queued tasks
    /// </summary>
    public sealed class BackgroundTaskProcessor : BackgroundService
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackgroundTaskProcessor> _logger;

        public BackgroundTaskProcessor(
            IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider,
            ILogger<BackgroundTaskProcessor> logger)
        {
            _taskQueue = taskQueue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Background task processor started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var workItem = await _taskQueue.DequeueAsync(stoppingToken).ConfigureAwait(false);

                    if (workItem != null)
                    {
                        await ProcessWorkItemAsync(workItem, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing background task");
                    await Task.Delay(1000, stoppingToken).ConfigureAwait(false); // Brief delay on error
                }
            }

            _logger.LogInformation("Background task processor stopped");
        }

        private async Task ProcessWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem, CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();

            try
            {
                await workItem(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background work item failed");
            }
        }
    }
}
