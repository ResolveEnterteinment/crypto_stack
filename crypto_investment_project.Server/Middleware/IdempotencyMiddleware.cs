using Application.Interfaces;
using Domain.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace crypto_investment_project.Server.Middleware
{
    /// <summary>
    /// Middleware for handling idempotent requests to prevent duplicate processing
    /// </summary>
    public class IdempotencyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<IdempotencyMiddleware> _logger;
        private readonly IdempotencySettings _settings;
        private readonly IMemoryCache _cache;
        private static readonly IdempotencyMetrics _metrics = new();

        public IdempotencyMiddleware(
            RequestDelegate next,
            ILogger<IdempotencyMiddleware> logger,
            IOptions<IdempotencySettings> settings,
            IMemoryCache cache)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            // Initialize metrics if enabled
            if (_settings.EnableMetrics)
            {
                IdempotencyMetricsExtensions.SetMetricsInstance(_metrics);
            }
        }

        public async Task InvokeAsync(HttpContext context, IIdempotencyService idempotencyService)
        {
            // Skip for non-idempotent methods or excluded paths
            if (!ShouldProcessIdempotency(context))
            {
                await _next(context);
                return;
            }

            var idempotencyKey = GetIdempotencyKey(context);

            // Validate key format if required
            if (!string.IsNullOrEmpty(idempotencyKey) && _settings.ValidateKeyFormat)
            {
                if (!Regex.IsMatch(idempotencyKey, _settings.KeyFormatPattern))
                {
                    await WriteErrorResponse(context, $"Invalid idempotency key format. Expected pattern: {_settings.KeyFormatPattern}", StatusCodes.Status400BadRequest);
                    return;
                }
            }

            // If no idempotency key provided, decide based on configuration
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                if (_settings.RequireIdempotencyKey)
                {
                    await WriteErrorResponse(context, "X-Idempotency-Key header is required for this operation", StatusCodes.Status400BadRequest);
                    return;
                }

                // Generate automatic key if enabled
                if (_settings.AutoGenerateKey)
                {
                    idempotencyKey = await GenerateIdempotencyKey(context);
                }
                else
                {
                    // No idempotency protection for this request
                    await _next(context);
                    return;
                }
            }

            // Add correlation for tracing
            var correlationId = Activity.Current?.Id ?? context.TraceIdentifier;

            try
            {
                // Update metrics if enabled
                if (_settings.EnableMetrics)
                {
                    _metrics.IncrementTotalRequests();
                }

                // Check for in-flight request (prevents race conditions)
                var lockKey = $"lock:{idempotencyKey}";
                if (!await AcquireLock(lockKey, context.RequestAborted))
                {
                    if (_settings.EnableMetrics)
                    {
                        _metrics.IncrementLockContentions();
                    }

                    _logger.LogWarning("Request already in progress for idempotency key: {Key}", idempotencyKey);
                    await WriteErrorResponse(context, "Request already in progress", StatusCodes.Status409Conflict);
                    return;
                }

                try
                {
                    // Check if this request was already processed
                    var (exists, cachedResponse) = await idempotencyService.GetResultAsync<IdempotentResponse>(idempotencyKey);

                    if (exists && cachedResponse != null)
                    {
                        if (_settings.EnableMetrics)
                        {
                            _metrics.IncrementCacheHits();
                        }

                        _logger.LogInformation("Returning cached response for idempotency key: {Key}", idempotencyKey);
                        await WriteCachedResponse(context, cachedResponse);
                        return;
                    }

                    if (_settings.EnableMetrics)
                    {
                        _metrics.IncrementCacheMisses();
                    }

                    await _next(context);
                }
                finally
                {
                    ReleaseLock(lockKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing idempotent request with key: {Key}", idempotencyKey);
                throw;
            }
        }

        private bool ShouldProcessIdempotency(HttpContext context)
        {
            // Use the settings methods for checking
            return _settings.ShouldProcessMethod(context.Request.Method) &&
                   _settings.ShouldProcessPath(context.Request.Path.Value);
        }

        private string GetIdempotencyKey(HttpContext context)
        {
            // Try to get from header first
            if (context.Request.Headers.TryGetValue(_settings.HeaderName, out var headerValue))
            {
                return headerValue.ToString();
            }

            // Try query parameter as fallback if enabled
            if (_settings.AllowQueryParameter &&
                context.Request.Query.TryGetValue(_settings.QueryParameterName, out var queryValue))
            {
                return queryValue.ToString();
            }

            return null;
        }

        private async Task<string> GenerateIdempotencyKey(HttpContext context)
        {
            var keyComponents = new StringBuilder();

            // Add user identifier
            var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
            keyComponents.Append(userId);

            // Add HTTP method and path
            keyComponents.Append($":{context.Request.Method}:{context.Request.Path}");

            // Add query string if present
            if (context.Request.QueryString.HasValue)
            {
                keyComponents.Append($":{context.Request.QueryString.Value}");
            }

            // Add request body hash for POST/PUT/PATCH
            if (context.Request.ContentLength > 0 &&
                (context.Request.Method == HttpMethods.Post ||
                 context.Request.Method == HttpMethods.Put ||
                 context.Request.Method == HttpMethods.Patch))
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;

                if (!string.IsNullOrEmpty(body))
                {
                    using var sha256 = SHA256.Create();
                    var bodyHash = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(body)));
                    keyComponents.Append($":{bodyHash}");
                }
            }

            return keyComponents.ToString();
        }

        private async Task<bool> AcquireLock(string lockKey, CancellationToken cancellationToken)
        {
            var attempts = 0;
            var maxAttempts = _settings.LockRetryAttempts;
            var retryDelay = TimeSpan.FromMilliseconds(_settings.LockRetryDelayMs);

            while (attempts < maxAttempts)
            {
                if (_cache.TryGetValue(lockKey, out _))
                {
                    attempts++;
                    if (attempts < maxAttempts)
                    {
                        await Task.Delay(retryDelay, cancellationToken);
                    }
                }
                else
                {
                    _cache.Set(lockKey, true, TimeSpan.FromSeconds(_settings.LockTimeoutSeconds));
                    return true;
                }
            }

            return false;
        }

        private void ReleaseLock(string lockKey)
        {
            _cache.Remove(lockKey);
        }

        private async Task WriteCachedResponse(HttpContext context, IdempotentResponse cachedResponse)
        {
            context.Response.StatusCode = cachedResponse.StatusCode;

            // Add cached response headers
            foreach (var header in cachedResponse.Headers)
            {
                if (!context.Response.Headers.ContainsKey(header.Key))
                {
                    context.Response.Headers[header.Key] = header.Value;
                }
            }

            // Add idempotency headers
            context.Response.Headers[_settings.CachedResponseHeader] = "true";
            context.Response.Headers[_settings.CachedTimestampHeader] = cachedResponse.Timestamp.ToString("O");

            if (!string.IsNullOrEmpty(cachedResponse.Body))
            {
                await context.Response.WriteAsync(cachedResponse.Body);
            }
        }

        private async Task WriteErrorResponse(HttpContext context, string message, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";

            object error;

            if (_settings.IncludeErrorDetails)
            {
                error = new
                {
                    error = new
                    {
                        message,
                        code = "IDEMPOTENCY_ERROR",
                        timestamp = DateTime.UtcNow,
                        traceId = Activity.Current?.Id ?? context.TraceIdentifier,
                        path = context.Request.Path.Value,
                        method = context.Request.Method
                    }
                };
            }
            else
            {
                error = new
                {
                    error = new
                    {
                        message,
                        code = "IDEMPOTENCY_ERROR",
                        timestamp = DateTime.UtcNow
                    }
                };
            }

            await context.Response.WriteAsync(JsonSerializer.Serialize(error));
        }
    }

    /// <summary>
    /// Represents a cached idempotent response
    /// </summary>
    public class IdempotentResponse
    {
        public int StatusCode { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Body { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Metrics tracking for idempotency system
    /// </summary>
    public class IdempotencyMetrics
    {
        private long _totalRequests;
        private long _cacheHits;
        private long _cacheMisses;
        private long _duplicateAttempts;
        private long _lockContentions;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private readonly object _lock = new();

        public long TotalRequests => _totalRequests;
        public long CacheHits => _cacheHits;
        public long CacheMisses => _cacheMisses;
        public long DuplicateAttempts => _duplicateAttempts;
        public long LockContentions => _lockContentions;

        public double HitRate => _totalRequests > 0 ? (double)_cacheHits / _totalRequests * 100 : 0;
        public TimeSpan Uptime => DateTime.UtcNow - _startTime;

        // These would be calculated from actual response time tracking
        public double AverageResponseTime => 45.2; // ms - placeholder
        public double CachedResponseTime => 2.3; // ms - placeholder
        public double FreshResponseTime => 87.5; // ms - placeholder

        public void IncrementTotalRequests()
        {
            Interlocked.Increment(ref _totalRequests);
        }

        public void IncrementCacheHits()
        {
            Interlocked.Increment(ref _cacheHits);
        }

        public void IncrementCacheMisses()
        {
            Interlocked.Increment(ref _cacheMisses);
        }

        public void IncrementDuplicateAttempts()
        {
            Interlocked.Increment(ref _duplicateAttempts);
        }

        public void IncrementLockContentions()
        {
            Interlocked.Increment(ref _lockContentions);
        }

        public void Reset()
        {
            lock (_lock)
            {
                _totalRequests = 0;
                _cacheHits = 0;
                _cacheMisses = 0;
                _duplicateAttempts = 0;
                _lockContentions = 0;
            }
        }
    }

    /// <summary>
    /// Extension to integrate metrics into middleware
    /// </summary>
    public static class IdempotencyMetricsExtensions
    {
        private static IdempotencyMetrics _metrics;

        public static void SetMetricsInstance(IdempotencyMetrics metrics)
        {
            _metrics = metrics;
        }

        public static IdempotencyMetrics GetMetrics()
        {
            return _metrics;
        }
    }
}