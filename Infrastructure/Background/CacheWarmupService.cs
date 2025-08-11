using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

public class CacheWarmupService : BackgroundService, ICacheWarmupService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CacheWarmupService> _logger;
    private readonly Channel<Guid> _userLoginQueue;
    
    public CacheWarmupService(
        IServiceScopeFactory scopeFactory,
        ILogger<CacheWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        var channel = Channel.CreateBounded<Guid>(options);
        _userLoginQueue = channel;
    }
    
    public void QueueUserCacheWarmup(Guid userId)
    {
        if (!_userLoginQueue.Writer.TryWrite(userId))
        {
            _logger.LogWarning("Failed to queue cache warmup for user {UserId} - queue is full", userId);
        }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var userId in _userLoginQueue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var balanceService = scope.ServiceProvider.GetRequiredService<IBalanceService>();
                var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();
                
                _logger.LogInformation("Starting cache warmup for user {UserId}", userId);
                
                // Warm up user-specific caches
                var warmupTasks = new[]
                {
                    balanceService.WarmupUserBalanceCacheAsync(userId),
                    dashboardService.WarmupUserCacheAsync(userId)
                };
                
                await Task.WhenAll(warmupTasks);
                
                _logger.LogInformation("Cache warmup completed for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache warmup failed for user {UserId}", userId);
            }
        }
    }
}