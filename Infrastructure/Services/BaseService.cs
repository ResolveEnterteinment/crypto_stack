// File: BaseService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Text.Json;

namespace Infrastructure.Services
{
    /// <summary>
    /// Provides common functionality for service classes.
    /// </summary>
    public abstract class BaseService<T>
    {
        protected readonly HttpClient _httpClient;
        protected readonly ILogger<T> _logger;
        protected readonly IConfiguration _config;
        protected readonly IMongoClient _mongoClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseService{T}"/> class.
        /// </summary>
        /// <param name="httpClient">An HttpClient instance for API calls.</param>
        /// <param name="logger">A logger instance for logging.</param>
        /// <param name="config">Application configuration.</param>
        /// <param name="mongoClient">A singleton MongoClient instance injected via DI.</param>
        protected BaseService(HttpClient httpClient, ILogger<T> logger, IConfiguration config, IMongoClient mongoClient)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = config;
            _mongoClient = mongoClient;
        }

        /// <summary>
        /// Performs a GET request and deserializes the response into the specified type.
        /// </summary>
        /// <typeparam name="U">The type to deserialize the response into.</typeparam>
        /// <param name="url">The URL for the GET request.</param>
        /// <returns>An instance of U representing the response data.</returns>
        protected async Task<U?> GetAsync<U>(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                return content is null ? throw new NullReferenceException($"Response content is null.") : JsonSerializer.Deserialize<U>(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during GET request to {Url}", url);
                return default;
            }
        }
    }
}
