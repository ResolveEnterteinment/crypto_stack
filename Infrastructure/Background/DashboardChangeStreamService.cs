using Application.Interfaces;
using Domain.Models.Balance;
using Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Background
{
    /// <summary>
    /// Background service that monitors MongoDB change streams and pushes updates to SignalR clients
    /// </summary>
    public class DashboardChangeStreamService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMongoCollection<BalanceData> _balances;
        private readonly ILogger<DashboardChangeStreamService> _logger;
        private readonly IHubContext<DashboardHub> _hubContext;

        public DashboardChangeStreamService(
            IServiceScopeFactory scopeFactory,
            IMongoDatabase database,
            ILogger<DashboardChangeStreamService> logger,
            IHubContext<DashboardHub> hubContext)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _balances = database.GetCollection<BalanceData>("balances");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dashboard Change Stream Service started");

            try
            {
                await StartChangeStreamMonitoring(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Dashboard Change Stream Service cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard Change Stream Service encountered an error");
            }
        }

        private async Task StartChangeStreamMonitoring(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BalanceData>>()
                        .Match(change =>
                            change.OperationType == ChangeStreamOperationType.Insert ||
                            change.OperationType == ChangeStreamOperationType.Update ||
                            change.OperationType == ChangeStreamOperationType.Replace);

                    var options = new ChangeStreamOptions 
                    { 
                        FullDocument = ChangeStreamFullDocumentOption.UpdateLookup 
                    };

                    using var cursor = _balances.Watch(pipeline, options, stoppingToken);

                    _logger.LogInformation("Started MongoDB change stream monitoring for dashboard updates");

                    await cursor.ForEachAsync(async change =>
                    {
                        if (stoppingToken.IsCancellationRequested) return;

                        try
                        {
                            await ProcessBalanceChange(change);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing balance change notification");
                        }
                    }, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Change stream monitoring cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in balance change stream. Retrying in 5 seconds...");
                    
                    try
                    {
                        await Task.Delay(5000, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task ProcessBalanceChange(ChangeStreamDocument<BalanceData> change)
        {
            try
            {
                var updatedBalance = change.FullDocument;
                if (updatedBalance == null)
                {
                    _logger.LogWarning("Received change notification with null document");
                    return;
                }

                var userId = updatedBalance.UserId;
                _logger.LogDebug("Processing balance change for user {UserId}", userId);

                // Use scoped services to avoid disposal issues
                using var scope = _scopeFactory.CreateScope();
                var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

                // Invalidate cache and get fresh data
                dashboardService.InvalidateDashboardCacheAsync(userId);

                // Fetch fresh data
                var dashboardData = await dashboardService.GetDashboardDataAsync(userId);
                if (dashboardData.IsSuccess && dashboardData.Data != null)
                {
                    // Push update to SignalR clients in user's group
                    await _hubContext.Clients.Group(userId.ToString())
                        .SendAsync("DashboardUpdate", dashboardData.Data);

                    _logger.LogDebug("Dashboard updated for user {UserId} due to balance change", userId);
                }
                else
                {
                    _logger.LogWarning("Failed to fetch fresh dashboard data for user {UserId}: {Error}",
                        userId, dashboardData.ErrorMessage);
                }
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning("Hub context disposed during balance change processing: {Message}", ex.Message);
                // This is expected during shutdown, don't treat as error
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing balance change notification");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Dashboard Change Stream Service is stopping");
            await base.StopAsync(cancellationToken);
        }
    }
}