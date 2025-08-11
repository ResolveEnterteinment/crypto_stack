using Microsoft.AspNetCore.Http;
using System.Net;

namespace Infrastructure.Services.Http
{
    public interface IHttpContextService
    {
        string GetClientIpAddress();
        string GetUserAgent();
        string GetRequestId();
        bool IsHttpContextAvailable();
    }

    public class HttpContextService : IHttpContextService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpContextService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        public string GetClientIpAddress()
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext == null)
                {
                    return "127.0.0.1"; // Fallback for non-HTTP contexts
                }

                // Check for forwarded headers first (for reverse proxy scenarios)
                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(forwardedFor))
                {
                    // X-Forwarded-For can contain multiple IPs, take the first one (original client)
                    var firstIp = forwardedFor.Split(',')[0].Trim();
                    if (IsValidIpAddress(firstIp))
                    {
                        return firstIp;
                    }
                }

                // Check X-Real-IP header (commonly used by nginx)
                var realIp = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(realIp) && IsValidIpAddress(realIp))
                {
                    return realIp;
                }

                // Check CF-Connecting-IP header (Cloudflare)
                var cloudflareIp = httpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(cloudflareIp) && IsValidIpAddress(cloudflareIp))
                {
                    return cloudflareIp;
                }

                // Fall back to direct connection IP
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
                if (!string.IsNullOrEmpty(remoteIp))
                {
                    // Convert IPv6 loopback to IPv4 for consistency
                    if (remoteIp == "::1")
                    {
                        return "127.0.0.1";
                    }
                    return remoteIp;
                }

                return "127.0.0.1"; // Final fallback
            }
            catch
            {
                return "127.0.0.1"; // Safe fallback on any error
            }
        }

        public string GetUserAgent()
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext == null)
                {
                    return "KYC-Service/1.0"; // Fallback for non-HTTP contexts
                }

                var userAgent = httpContext.Request.Headers["User-Agent"].FirstOrDefault();
                
                // Sanitize and validate user agent
                if (string.IsNullOrWhiteSpace(userAgent))
                {
                    return "Unknown";
                }

                // Truncate extremely long user agents to prevent abuse
                if (userAgent.Length > 512)
                {
                    userAgent = userAgent.Substring(0, 512) + "...";
                }

                // Basic sanitization - remove control characters
                userAgent = System.Text.RegularExpressions.Regex.Replace(userAgent, @"[\x00-\x1F\x7F]", "");

                return string.IsNullOrWhiteSpace(userAgent) ? "Unknown" : userAgent;
            }
            catch
            {
                return "Unknown"; // Safe fallback on any error
            }
        }

        public string GetRequestId()
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                return httpContext?.Request.Headers["X-Request-ID"].FirstOrDefault() 
                    ?? httpContext?.TraceIdentifier 
                    ?? Guid.NewGuid().ToString("N")[..8];
            }
            catch
            {
                return Guid.NewGuid().ToString("N")[..8];
            }
        }

        public bool IsHttpContextAvailable()
        {
            return _httpContextAccessor.HttpContext != null;
        }

        /// <summary>
        /// Validates if a string is a valid IP address (IPv4 or IPv6)
        /// </summary>
        private static bool IsValidIpAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            return IPAddress.TryParse(ipAddress, out _);
        }
    }
}