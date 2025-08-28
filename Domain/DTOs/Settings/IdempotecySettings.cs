using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;

namespace Domain.Settings
{
    /// <summary>
    /// Configuration settings for idempotency middleware
    /// </summary>
    public class IdempotencySettings
    {
        public const string SectionName = "Idempotency";

        /// <summary>
        /// The HTTP header name used to pass idempotency keys
        /// </summary>
        [Required]
        public string HeaderName { get; set; } = "X-Idempotency-Key";

        /// <summary>
        /// The query parameter name for idempotency keys (if enabled)
        /// </summary>
        public string QueryParameterName { get; set; } = "idempotencyKey";

        /// <summary>
        /// Whether to allow idempotency keys via query parameters
        /// </summary>
        public bool AllowQueryParameter { get; set; } = false;

        /// <summary>
        /// Whether to require an explicit idempotency key for configured endpoints
        /// </summary>
        public bool RequireIdempotencyKey { get; set; } = false;

        /// <summary>
        /// Whether to automatically generate keys from request content when not provided
        /// </summary>
        public bool AutoGenerateKey { get; set; } = false;

        /// <summary>
        /// HTTP methods that should be processed for idempotency
        /// </summary>
        public List<string> Methods { get; set; } = new() { "POST", "PUT", "PATCH", "DELETE" };

        /// <summary>
        /// Request paths that should be excluded from idempotency processing
        /// </summary>
        public List<string> ExcludedPaths { get; set; } = new()
        {
            "/health",
            "/swagger",
            "/api/diagnostic",
            "/api/v1/csrf",
            "/api/v1/auth/login",
            "/api/v1/auth/logout",
            "/api/v1/auth/refresh",
            "/hubs"
        };

        /// <summary>
        /// Request paths that should be explicitly included for idempotency processing
        /// If specified, only these paths will be processed
        /// </summary>
        public List<string> IncludedPaths { get; set; } = new();

        /// <summary>
        /// Timeout in seconds for distributed locks
        /// </summary>
        [Range(1, 300)]
        public int LockTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Number of retry attempts when acquiring a lock
        /// </summary>
        [Range(1, 10)]
        public int LockRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Delay in milliseconds between lock retry attempts
        /// </summary>
        [Range(10, 5000)]
        public int LockRetryDelayMs { get; set; } = 100;

        /// <summary>
        /// Cache expiration time in minutes for successful responses (2xx)
        /// </summary>
        [Range(1, 1440)]
        public int SuccessExpirationMinutes { get; set; } = 60;

        /// <summary>
        /// Cache expiration time in minutes for client error responses (4xx)
        /// </summary>
        [Range(1, 60)]
        public int ClientErrorExpirationMinutes { get; set; } = 5;

        /// <summary>
        /// Default cache expiration time in minutes for other responses
        /// </summary>
        [Range(1, 120)]
        public int DefaultExpirationMinutes { get; set; } = 15;

        /// <summary>
        /// Whether to include response body in cached responses
        /// </summary>
        public bool CacheResponseBody { get; set; } = true;

        /// <summary>
        /// Maximum response body size in bytes to cache (to prevent memory issues)
        /// </summary>
        [Range(1024, 10485760)] // 1KB to 10MB
        public int MaxResponseBodySize { get; set; } = 1048576; // 1MB default

        /// <summary>
        /// Whether to validate idempotency key format (UUID)
        /// </summary>
        public bool ValidateKeyFormat { get; set; } = false;

        /// <summary>
        /// Regular expression pattern for valid idempotency keys (if validation enabled)
        /// </summary>
        public string KeyFormatPattern { get; set; } = @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$";

        /// <summary>
        /// Whether to include detailed error information in responses
        /// </summary>
        public bool IncludeErrorDetails { get; set; } = true;

        /// <summary>
        /// Whether to enable metrics collection
        /// </summary>
        public bool EnableMetrics { get; set; } = true;

        /// <summary>
        /// Whether to enable detailed logging
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = false;

        /// <summary>
        /// Custom header to indicate cached responses
        /// </summary>
        public string CachedResponseHeader { get; set; } = "X-Idempotent-Response";

        /// <summary>
        /// Custom header to indicate when response was cached
        /// </summary>
        public string CachedTimestampHeader { get; set; } = "X-Idempotent-Cached-At";

        /// <summary>
        /// List of status codes that should be cached
        /// </summary>
        public List<int> CacheableStatusCodes { get; set; } = new();

        /// <summary>
        /// List of status codes that should NOT be cached (overrides default behavior)
        /// </summary>
        public List<int> NonCacheableStatusCodes { get; set; } = new() { 500, 502, 503, 504 };

        /// <summary>
        /// Validate the settings
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(HeaderName))
            {
                throw new InvalidOperationException("HeaderName cannot be empty");
            }

            if (Methods == null || !Methods.Any())
            {
                throw new InvalidOperationException("At least one HTTP method must be configured");
            }

            if (RequireIdempotencyKey && AutoGenerateKey)
            {
                // This is actually valid - require key but generate if not provided
                // Log warning instead of throwing
                Console.WriteLine("Warning: Both RequireIdempotencyKey and AutoGenerateKey are true. Auto-generation will be used as fallback.");
            }

            if (LockTimeoutSeconds < LockRetryAttempts * (LockRetryDelayMs / 1000.0))
            {
                Console.WriteLine("Warning: Lock timeout may be too short for configured retry attempts");
            }

            if (ValidateKeyFormat && string.IsNullOrWhiteSpace(KeyFormatPattern))
            {
                throw new InvalidOperationException("KeyFormatPattern must be provided when ValidateKeyFormat is enabled");
            }

            if (MaxResponseBodySize < 1024)
            {
                throw new InvalidOperationException("MaxResponseBodySize must be at least 1KB");
            }
        }

        /// <summary>
        /// Get the effective expiration for a given status code
        /// </summary>
        public TimeSpan GetExpirationForStatusCode(int statusCode)
        {
            return statusCode switch
            {
                >= 200 and < 300 => TimeSpan.FromMinutes(SuccessExpirationMinutes),
                >= 400 and < 500 => TimeSpan.FromMinutes(ClientErrorExpirationMinutes),
                _ => TimeSpan.FromMinutes(DefaultExpirationMinutes)
            };
        }

        /// <summary>
        /// Check if a status code should be cached
        /// </summary>
        public bool ShouldCacheStatusCode(int statusCode)
        {
            // Check explicit non-cacheable list first
            if (NonCacheableStatusCodes.Contains(statusCode))
            {
                return false;
            }

            // If explicit cacheable list is provided, use it
            if (CacheableStatusCodes.Any())
            {
                return CacheableStatusCodes.Contains(statusCode);
            }

            // Default behavior: cache 2xx and 4xx, don't cache 5xx
            return statusCode >= 200 && statusCode < 300;
        }

        /// <summary>
        /// Check if a path should be processed for idempotency
        /// </summary>
        public bool ShouldProcessPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalizedPath = path.ToLowerInvariant();

            // Check excluded paths first
            if (ExcludedPaths.Any(excludedPath =>
                normalizedPath.StartsWith(excludedPath.ToLowerInvariant())))
            {
                return false;
            }

            // If included paths are specified, only process those
            if (IncludedPaths.Any())
            {
                return IncludedPaths.Any(includedPath =>
                    normalizedPath.StartsWith(includedPath.ToLowerInvariant()));
            }

            // Process all non-excluded paths
            return true;
        }

        /// <summary>
        /// Check if a method should be processed for idempotency
        /// </summary>
        public bool ShouldProcessMethod(string method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return false;
            }

            return Methods.Contains(method, StringComparer.OrdinalIgnoreCase);
        }
    }
}