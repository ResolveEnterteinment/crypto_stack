// Infrastructure/Background/OldPaymentCleanupService.cs
using Application.Interfaces.Payment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Background
{
    public class OldPaymentCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<OldPaymentCleanupBackgroundService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromDays(1); // Run daily
        private readonly int _failedPaymentRetentionDays = 90; // Keep failed payments for 90 days

        public OldPaymentCleanupBackgroundService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<OldPaymentCleanupBackgroundService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Old payment cleanup service is starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanupOldPayments(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning up old payments");
                }

                // Wait until next execution time
                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Old payment cleanup service is stopping");
        }

        private async Task CleanupOldPayments(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting cleanup of old failed payments");

            using var scope = _serviceScopeFactory.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

            var cutoffDate = DateTime.UtcNow.AddDays(-_failedPaymentRetentionDays);
            var filter = Builders<Domain.Models.Payment.PaymentData>.Filter.And(
                Builders<Domain.Models.Payment.PaymentData>.Filter.Eq(p => p.Status, Domain.Constants.Payment.PaymentStatus.Failed),
                Builders<Domain.Models.Payment.PaymentData>.Filter.Lt(p => p.CreatedAt, cutoffDate)
            );

            try
            {
                // This assumes you have a DeleteManyAsync method in your base service
                var deleteResult = await paymentService.DeleteManyAsync(filter);

                if (deleteResult.IsSuccess)
                {
                    _logger.LogInformation("Successfully cleaned up {Count} old failed payments",
                        deleteResult.Data?.ModifiedCount ?? 0);
                }
                else
                {
                    _logger.LogWarning("Failed to clean up old payments: {ErrorMessage}",
                        deleteResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old payments");
            }
        }
    }
}