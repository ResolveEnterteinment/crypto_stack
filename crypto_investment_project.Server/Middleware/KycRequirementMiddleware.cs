using Application.Contracts.Requests.Withdrawal;
using Application.Interfaces.Exchange;
using Application.Interfaces.KYC;
using Application.Interfaces.Withdrawal;
using crypto_investment_project.Server.Middleware;
using Domain.Constants.KYC;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace crypto_investment_project.Server.Middleware
{
    public class KycRequirementMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<KycRequirementMiddleware> _logger;
        private readonly KycMiddlewareOptions _options;

        // Route-to-KYC level mapping
        private readonly Dictionary<string, KycRequirement> _routeRequirements = new()
        {            
            // Withdrawal endpoints
            { "/api/withdrawal/request", new KycRequirement { Level = KycLevel.Basic, RequireActiveSession = false, CheckLimits = true } },
        };

        public KycRequirementMiddleware(
            RequestDelegate next,
            IMemoryCache cache,
            ILogger<KycRequirementMiddleware> logger,
            IOptions<KycMiddlewareOptions> options)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task InvokeAsync(HttpContext context, IKycService kycService, IKycSessionService kycSessionService, IExchangeService exchangeService, IWithdrawalService withdrawalService)
        {
            try
            {
                // Skip middleware for non-API requests
                if (!context.Request.Path.StartsWithSegments("/api"))
                {
                    await _next(context);
                    return;
                }

                // Skip for health checks and public endpoints
                if (IsPublicEndpoint(context.Request.Path))
                {
                    await _next(context);
                    return;
                }

                // Skip for unauthenticated requests (auth middleware will handle)
                if (!context.User.Identity?.IsAuthenticated == true)
                {
                    await _next(context);
                    return;
                }

                // Skip for admin users (but still log the access)
                if (context.User.IsInRole("ADMIN"))
                {
                    await LogAdminAccess(context);
                    await _next(context);
                    return;
                }

                // Get user ID
                var userId = GetUserId(context);
                if (!userId.HasValue)
                {
                    await WriteUnauthorizedResponse(context, "Invalid user authentication");
                    return;
                }

                // Check if the route requires KYC
                var requirement = GetKycRequirement(context.Request.Path);
                if (requirement == null)
                {
                    await _next(context);
                    return;
                }

                // Perform KYC verification
                var verificationResult = await PerformKycVerification(context, kycService, userId.Value, requirement);
                if (!verificationResult.IsVerified)
                {
                    await WriteKycRequiredResponse(context, verificationResult);
                    return;
                }

                // Additional security checks
                if (requirement.RequireActiveSession)
                {
                    var sessionValid = await ValidateActiveSession(context, kycService, kycSessionService, userId.Value);
                    if (!sessionValid)
                    {
                        await WriteSessionRequiredResponse(context);
                        return;
                    }
                }

                // Check withdrawal limits if required
                if (requirement.CheckLimits)
                {
                    var limitsValid = await CheckWithdrawalLimits(context, kycService, exchangeService, withdrawalService, userId.Value);
                    if (!limitsValid)
                    {
                        await WriteLimitsExceededResponse(context);
                        return;
                    }
                }

                // Log successful KYC verification
                await LogKycSuccess(context, userId.Value, requirement);

                // Continue with the request
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in KYC middleware");
                await WriteInternalErrorResponse(context);
            }
        }

        private async Task<KycVerificationResult> PerformKycVerification(
            HttpContext context,
            IKycService kycService,
            Guid userId,
            KycRequirement requirement)
        {
            try
            {
                // Check cache first
                var cacheKey = $"kyc_verification_{userId}_{requirement.Level}";
                if (_cache.TryGetValue(cacheKey, out KycVerificationResult cachedResult))
                {
                    return cachedResult;
                }

                // Perform KYC verification
                var verificationResult = await kycService.IsUserVerifiedAsync(userId, requirement.Level);

                var result = new KycVerificationResult
                {
                    IsVerified = verificationResult.IsSuccess && verificationResult.Data,
                    RequiredLevel = requirement.Level,
                    UserId = userId,
                    CheckedAt = DateTime.UtcNow,
                    ErrorMessage = verificationResult.IsSuccess ? null : verificationResult.ErrorMessage
                };

                // Additional checks for advanced verification
                if (requirement.Level == KycLevel.Advanced)
                {
                    var tradingEligible = await kycService.IsUserEligibleForTrading(userId);
                    result.IsVerified = result.IsVerified && tradingEligible.IsSuccess && tradingEligible.Data;
                }

                // Cache the result for a short time
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error verifying KYC for user {userId}");
                return new KycVerificationResult
                {
                    IsVerified = false,
                    RequiredLevel = requirement.Level,
                    UserId = userId,
                    CheckedAt = DateTime.UtcNow,
                    ErrorMessage = "KYC verification failed"
                };
            }
        }

        private async Task<bool> ValidateActiveSession(HttpContext context, IKycService kycService, IKycSessionService kycSessionService, Guid userId)
        {
            try
            {
                // Check for active KYC session if required
                var sessionHeader = context.Request.Headers["X-KYC-Session"].FirstOrDefault();
                if (string.IsNullOrEmpty(sessionHeader))
                {
                    _logger.LogWarning("Missing X-KYC-Session header for user {UserId}", userId);
                    return false;
                }

                if (!Guid.TryParse(sessionHeader, out var sessionId))
                {
                    _logger.LogWarning("Invalid X-KYC-Session header format for user {UserId}: {SessionHeader}", userId, sessionHeader);
                    return false;
                }

                // Validate session through the KYC service
                var sessionValidation = await kycSessionService.ValidateSessionAsync(sessionId, userId);
                if (!sessionValidation.IsSuccess)
                {
                    _logger.LogWarning("Session validation failed for user {UserId}, session {SessionId}: {Error}",
                        userId, sessionId, sessionValidation.ErrorMessage);
                    return false;
                }

                var session = sessionValidation.Data;

                // Additional security checks
                var clientIp = context.Connection.RemoteIpAddress?.ToString();
                var userAgent = context.Request.Headers["User-Agent"].FirstOrDefault();

                // Check for suspicious IP changes (optional - configure based on security requirements)
                if (!string.IsNullOrEmpty(session.SecurityContext?.IpAddress) &&
                    session.SecurityContext.IpAddress != clientIp)
                {
                    _logger.LogWarning("IP address change detected for session {SessionId}: {OldIp} -> {NewIp}",
                        sessionId, session.SecurityContext.IpAddress, clientIp);

                    // Optionally invalidate session on IP change for high-security operations
                    if (context.Request.Path.StartsWithSegments("/api/withdrawal") ||
                        context.Request.Path.StartsWithSegments("/api/exchange/large-order"))
                    {
                        await kycSessionService.InvalidateSessionAsync(sessionId, userId, "IP address change detected");
                        return false;
                    }
                }

                // Store session data in context for use by controllers
                context.Items["KYC_SESSION"] = session;
                context.Items["KYC_SESSION_ID"] = sessionId;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating session for user {userId}");
                return false;
            }
        }

        private async Task<bool> CheckWithdrawalLimits(HttpContext context, IKycService kycService, IExchangeService exchangeService, IWithdrawalService withdrawalService, Guid userId)
        {
            try
            {
                // Read request body to get withdrawal amount
                context.Request.EnableBuffering();
                var body = await ReadRequestBody(context.Request);

                if (string.IsNullOrEmpty(body))
                {
                    throw new ArgumentException("Invalid request body format.");
                }
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var withdrawalRequest = JsonSerializer.Deserialize<CryptoWithdrawalRequest>(body, options);
                if (!(withdrawalRequest?.Amount > 0m))
                {
                    throw new ArgumentException("Invalid withdrawal amount");
                }

                if (string.IsNullOrEmpty(withdrawalRequest?.Currency))
                {
                    throw new ArgumentException("Withdrawal currency is required");
                }

                var canUserWithdrawResult = await withdrawalService.CanUserWithdrawAsync(userId, withdrawalRequest.Amount, withdrawalRequest.Currency);

                if(canUserWithdrawResult == null || !canUserWithdrawResult.IsSuccess || canUserWithdrawResult.Data == false)
                {
                    return false; // User cannot withdraw
                }

                return true; // Within limits

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking withdrawal limits for user {userId}");
                return false; // Fail safe - deny on error
            }
        }

        private async Task<string> ReadRequestBody(HttpRequest request)
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;
            return body;
        }

        private KycRequirement? GetKycRequirement(PathString path)
        {
            // Direct path match
            if (_routeRequirements.TryGetValue(path.Value, out var requirement))
            {
                return requirement;
            }

            // Pattern matching for parameterized routes
            foreach (var kvp in _routeRequirements)
            {
                if (IsRouteMatch(path.Value, kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private bool IsRouteMatch(string requestPath, string routePattern)
        {
            // Simple pattern matching - in production, use a proper route matcher
            if (routePattern.Contains("{"))
            {
                var pattern = routePattern.Replace("{id}", @"\d+").Replace("{guid}", @"[a-fA-F0-9\-]+");
                return System.Text.RegularExpressions.Regex.IsMatch(requestPath, pattern);
            }

            return requestPath.StartsWith(routePattern, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPublicEndpoint(PathString path)
        {
            var publicEndpoints = new[]
            {
                "/api/auth/login",
                "/api/auth/register",
                "/api/auth/confirm-email",
                "/api/auth/reset-password",
                "/api/kyc/requirements",
                "/api/health",
                "/api/status",
                "/api/version",
            };

            return publicEndpoints.Any(endpoint => path.StartsWithSegments(endpoint));
        }

        private Guid? GetUserId(HttpContext context)
        {
            var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }

        private async Task WriteUnauthorizedResponse(HttpContext context, string message)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = "UNAUTHORIZED",
                message = message,
                timestamp = DateTime.UtcNow,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }

        private async Task WriteKycRequiredResponse(HttpContext context, KycVerificationResult result)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = "KYC_REQUIRED",
                message = "KYC verification required to access this resource",
                requiredLevel = result.RequiredLevel,
                currentStatus = result.ErrorMessage ?? "Not verified",
                kycUrl = "/kyc/verify",
                timestamp = DateTime.UtcNow,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            await LogKycFailure(context, result);
        }

        private async Task WriteSessionRequiredResponse(HttpContext context)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = "ACTIVE_SESSION_REQUIRED",
                message = "An active KYC session is required for this operation",
                sessionUrl = "/api/kyc/session",
                timestamp = DateTime.UtcNow,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }

        private async Task WriteLimitsExceededResponse(HttpContext context)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = "LIMITS_EXCEEDED",
                message = "Transaction limits exceeded for current KYC level",
                upgradeUrl = "/kyc/upgrade",
                timestamp = DateTime.UtcNow,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }

        private async Task WriteInternalErrorResponse(HttpContext context)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = "INTERNAL_ERROR",
                message = "An error occurred while processing your request",
                timestamp = DateTime.UtcNow,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }

        private async Task LogAdminAccess(HttpContext context)
        {
            var userId = GetUserId(context);
            var logEntry = new SecurityLogEntry
            {
                EventType = "ADMIN_ACCESS",
                UserId = userId,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault(),
                Path = context.Request.Path,
                Method = context.Request.Method,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Admin access: {UserId} accessed {Path} from {IpAddress}",
                userId, context.Request.Path, logEntry.IpAddress);
        }

        private async Task LogKycSuccess(HttpContext context, Guid userId, KycRequirement requirement)
        {
            var logEntry = new SecurityLogEntry
            {
                EventType = "KYC_SUCCESS",
                UserId = userId,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault(),
                Path = context.Request.Path,
                Method = context.Request.Method,
                Details = $"KYC level {requirement.Level} verified",
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("KYC verification successful: {UserId} - Level {Level} - Path {Path}",
                userId, requirement.Level, context.Request.Path);
        }

        private async Task LogKycFailure(HttpContext context, KycVerificationResult result)
        {
            var logEntry = new SecurityLogEntry
            {
                EventType = "KYC_FAILURE",
                UserId = result.UserId,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault(),
                Path = context.Request.Path,
                Method = context.Request.Method,
                Details = $"KYC verification failed: {result.ErrorMessage}",
                Timestamp = DateTime.UtcNow
            };

            _logger.LogWarning("KYC verification failed: {UserId} - Required Level {Level} - Path {Path} - Error {Error}",
                result.UserId, result.RequiredLevel, context.Request.Path, result.ErrorMessage);
        }
    }

    // Supporting classes
    public class KycRequirement
    {
        public string Level { get; set; } = string.Empty;
        public bool RequireActiveSession { get; set; }
        public bool CheckLimits { get; set; }
    }

    public class KycVerificationResult
    {
        public bool IsVerified { get; set; }
        public string RequiredLevel { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public DateTime CheckedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class KycMiddlewareOptions
    {
        public bool EnableCaching { get; set; } = true;
        public int CacheTimeoutMinutes { get; set; } = 5;
        public bool EnableDetailedLogging { get; set; } = true;
        public bool EnableSecurityHeaders { get; set; } = true;
    }

    public class SecurityLogEntry
    {
        public string EventType { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? Path { get; set; }
        public string? Method { get; set; }
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

// Extension method for registering the middleware
    public static class KycMiddlewareExtensions
    {
        public static IApplicationBuilder UseKycRequirement(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<KycRequirementMiddleware>();
        }
    }


// Configuration extension

    public static class KycMiddlewareServiceExtensions
    {
        public static IServiceCollection AddKycMiddleware(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<KycMiddlewareOptions>(
                configuration.GetSection("KycMiddleware"));

            return services;
        }
    }
