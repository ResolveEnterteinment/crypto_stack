using Domain.DTOs.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.KYC
{
    public class OpenSanctionsService : BackgroundService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OpenSanctionsService> _logger;
        private readonly string _dataDirectory;
        private readonly TimeSpan _refreshInterval;

        public OpenSanctionsService(
            HttpClient httpClient,
            IOptions<KycSettings> settings,
            ILogger<OpenSanctionsService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var minimalKycSettings = settings.Value;
            _dataDirectory = minimalKycSettings.SanctionsDataDirectory ??
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SanctionsData");
            _refreshInterval = TimeSpan.FromHours(minimalKycSettings.SanctionsUpdateIntervalHours);

            // Ensure directory exists
            _ = Directory.CreateDirectory(_dataDirectory);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Updating sanctions data...");
                    await DownloadLatestSanctionsData();
                    _logger.LogInformation("Sanctions data updated successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating sanctions data");
                }

                await Task.Delay(_refreshInterval, stoppingToken);
            }
        }

        private async Task DownloadLatestSanctionsData()
        {
            // OpenSanctions datasets URL
            var datasetUrl = "https://data.opensanctions.org/datasets/latest/sanctions.json";

            var response = await _httpClient.GetAsync(datasetUrl);
            _ = response.EnsureSuccessStatusCode();

            var tempFilePath = Path.Combine(_dataDirectory, "sanctions_latest.json");
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream);
            }

            // Use atomic file operations to prevent partial reads
            var finalFilePath = Path.Combine(_dataDirectory, "sanctions.json");
            if (File.Exists(finalFilePath))
            {
                var backupPath = Path.Combine(_dataDirectory, "sanctions.json.bak");
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                File.Move(finalFilePath, backupPath);
            }

            File.Move(tempFilePath, finalFilePath);
        }
    }
}