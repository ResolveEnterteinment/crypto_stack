using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.DTOs.Settings;
using Domain.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Background
{
    public class OpenSanctionsDataImporter : BackgroundService
    {
        private readonly HttpClient _httpClient;
        private readonly ILoggingService _logger;
        private readonly ICrudRepository<SanctionedEntity> _repository;
        private readonly KycSettings _settings;
        private readonly PeriodicTimer _timer;

        public OpenSanctionsDataImporter(
            IHttpClientFactory httpClientFactory,
            IOptions<KycSettings> settings,
            ICrudRepository<SanctionedEntity> repository,
            ILoggingService logger)
        {
            _httpClient = httpClientFactory.CreateClient("OpenSanctions");
            _settings = settings.Value;
            _repository = repository;
            _logger = logger;
            _timer = new PeriodicTimer(TimeSpan.FromDays(1)); // Update daily
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OpenSanctions data importer started");

            try
            {
                // Initial import
                await ImportDataAsync(stoppingToken);

                // Periodic imports
                while (await _timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
                {
                    await ImportDataAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the service is stopping
                _logger.LogInformation("OpenSanctions data importer stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in OpenSanctions data importer: {ErrorMEssage}", ex.Message);
            }
        }

        private async Task ImportDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Importing OpenSanctions data");

                // Set up API request
                string endpoint = $"{_settings.OpenSanctionsEndpoint}/entities";

                if (!string.IsNullOrEmpty(_settings.OpenSanctionsApiKey))
                {
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"ApiKey {_settings.OpenSanctionsApiKey}");
                }

                // Download entities in batches
                int offset = 0;
                const int limit = 1000;
                int totalImported = 0;
                bool hasMore = true;

                while (hasMore && !cancellationToken.IsCancellationRequested)
                {
                    HttpResponseMessage response = await _httpClient.GetAsync(
                        $"{endpoint}?limit={limit}&offset={offset}",
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("Failed to fetch OpenSanctions data: {StatusCode}", response.StatusCode);
                        break;
                    }

                    OpenSanctionsResponse? data = await response.Content.ReadFromJsonAsync<OpenSanctionsResponse>(cancellationToken);
                    if (data?.Results == null || data.Results.Count == 0)
                    {
                        hasMore = false;
                        continue;
                    }

                    // Process batch of entities
                    List<SanctionedEntity> entities = data.Results.Select(MapToSanctionedEntity).ToList();

                    // TODO: Save entities to database - use bulk operations
                    // In a real implementation, use efficient bulk insert/update
                    // For example with MongoDB:
                    // await _repository.BulkInsertAsync(entities, cancellationToken);

                    totalImported += entities.Count;
                    offset += limit;

                    _logger.LogInformation("Imported {Count} entities, total: {Total}",
                        entities.Count, totalImported);

                    // Check if we've reached the end
                    hasMore = data.Results.Count >= limit;
                }

                _logger.LogInformation("OpenSanctions data import completed, total entities: {Total}", totalImported);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error importing OpenSanctions data: {ErrorMessage}", ex.Message);
            }
        }

        private SanctionedEntity MapToSanctionedEntity(OpenSanctionsEntity entity)
        {
            return new SanctionedEntity
            {
                Id = Guid.NewGuid(),
                ExternalId = entity.Id,
                Schema = entity.Schema,
                Name = entity.Caption,
                Aliases = entity.Names?.Select(n => n.Text).ToList() ?? [],
                BirthDate = entity.Properties?.BirthDate?.FirstOrDefault(),
                Countries = entity.Properties?.Country?.ToList() ?? [],
                IsPep = entity.Schema == "Person" && (entity.Properties?.Topics?.Contains("pep") ?? false),
                IsSanctioned = entity.Properties?.Topics?.Contains("sanction") ?? false,
                UpdatedAt = DateTime.UtcNow,
                RawData = JsonSerializer.Serialize(entity)
            };
        }
    }

    public class OpenSanctionsResponse
    {
        public List<OpenSanctionsEntity> Results { get; set; } = [];
        public int Limit { get; set; }
        public int Offset { get; set; }
        public int Total { get; set; }
    }

    public class OpenSanctionsEntity
    {
        public required string Id { get; set; }
        public required string Schema { get; set; }
        public required string Caption { get; set; }
        public List<OpenSanctionsName> Names { get; set; } = [];
        public required OpenSanctionsProperties Properties { get; set; }
    }

    public class OpenSanctionsName
    {
        public required string Text { get; set; }
    }

    public class OpenSanctionsProperties
    {
        public List<string> BirthDate { get; set; } = [];
        public List<string> Country { get; set; } = [];
        public List<string> Topics { get; set; } = [];
    }

    public class SanctionedEntity : BaseEntity
    {
        public required string ExternalId { get; set; }
        public required string Schema { get; set; }
        public required string Name { get; set; }
        public List<string> Aliases { get; set; } = [];
        public required string BirthDate { get; set; }
        public List<string> Countries { get; set; } = [];
        public bool IsPep { get; set; }
        public bool IsSanctioned { get; set; }
        public required string RawData { get; set; }
    }
}