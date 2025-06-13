using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Collections.Concurrent;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class KeyExchangeController : ControllerBase
    {
        private readonly ILogger<KeyExchangeController> _logger;

        // Static in-memory storage for encryption keys (with expiration)
        private static readonly ConcurrentDictionary<string, (string Key, DateTime Expires)> _encryptionKeys = new();

        // Cleanup timer to remove expired keys
        private static readonly Timer _cleanupTimer = new Timer(CleanupExpiredKeys, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        public KeyExchangeController(ILogger<KeyExchangeController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Initializes encryption by providing a session-specific encryption key
        /// </summary>
        /// <returns>Base64-encoded 256-bit encryption key</returns>
        [HttpPost("initialize")]
        [AllowAnonymous]
        public IActionResult InitializeEncryption()
        {
            try
            {
                _logger.LogInformation("🔑 Key exchange initialization requested from {IP}",
                    HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                // Generate a 256-bit (32-byte) key for AES encryption
                var keyBytes = GenerateSecureKey();
                var keyBase64 = Convert.ToBase64String(keyBytes);

                _logger.LogDebug("✅ Generated {KeyLength}-byte encryption key", keyBytes.Length);

                // Create a client identifier
                var clientId = GenerateClientId(HttpContext);

                // Store the key with expiration (30 minutes)
                _encryptionKeys[clientId] = (keyBase64, DateTime.UtcNow.AddMinutes(30));

                _logger.LogInformation("🔐 Encryption key stored for client {ClientId}, expires at {Expires}",
                    clientId, DateTime.UtcNow.AddMinutes(30));

                // Return the raw key (NOT encrypted) for client initialization
                var response = new
                {
                    key = keyBase64,
                    clientId = clientId,
                    keyLength = keyBytes.Length,
                    expiresAt = DateTime.UtcNow.AddMinutes(30)
                };

                _logger.LogInformation("✅ Key exchange successful for client {ClientId}", clientId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize encryption key exchange");

                // Return detailed error information for debugging
                return StatusCode(500, new
                {
                    error = "Failed to initialize encryption",
                    message = ex.Message,
                    type = ex.GetType().Name,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Debug endpoint to check key exchange status
        /// </summary>
        [HttpGet("status")]
        [AllowAnonymous]
        public IActionResult GetStatus()
        {
            try
            {
                var clientId = GenerateClientId(HttpContext);
                var hasKey = _encryptionKeys.ContainsKey(clientId);
                var activeKeys = _encryptionKeys.Count;

                return Ok(new
                {
                    clientId = clientId,
                    hasActiveKey = hasKey,
                    totalActiveKeys = activeKeys,
                    timestamp = DateTime.UtcNow,
                    serverTime = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get key exchange status");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Gets the current client's encryption key (for server-side operations)
        /// This is NOT an API endpoint - it's a helper method for middleware
        /// </summary>
        [NonAction]
        public string? GetClientEncryptionKey(HttpContext context)
        {
            try
            {
                var clientId = GenerateClientId(context);

                if (_encryptionKeys.TryGetValue(clientId, out var keyData))
                {
                    if (keyData.Expires > DateTime.UtcNow)
                    {
                        return keyData.Key;
                    }
                    else
                    {
                        // Remove expired key
                        _encryptionKeys.TryRemove(clientId, out _);
                        _logger.LogDebug("🗑️ Removed expired key for client {ClientId}", clientId);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to retrieve client encryption key");
                return null;
            }
        }

        /// <summary>
        /// Generates a unique client identifier based on request characteristics
        /// </summary>
        private string GenerateClientId(HttpContext context)
        {
            try
            {
                // Combine IP address and User-Agent for a reasonably unique identifier
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var userAgent = context.Request.Headers.UserAgent.ToString();
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                // Create a hash for privacy and consistency
                var combined = $"{ipAddress}:{userAgent}:{timestamp}";
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
                return Convert.ToBase64String(hashBytes)[..16]; // Take first 16 characters
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to generate client ID, using fallback");
                return Guid.NewGuid().ToString("N")[..16];
            }
        }

        /// <summary>
        /// Generates a cryptographically secure 256-bit encryption key
        /// </summary>
        private static byte[] GenerateSecureKey()
        {
            using var rng = RandomNumberGenerator.Create();
            var key = new byte[32]; // 256 bits
            rng.GetBytes(key);
            return key;
        }

        /// <summary>
        /// Cleanup method to remove expired encryption keys
        /// </summary>
        private static void CleanupExpiredKeys(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = _encryptionKeys
                    .Where(kvp => kvp.Value.Expires <= now)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _encryptionKeys.TryRemove(key, out _);
                }

                if (expiredKeys.Count > 0)
                {
                    Console.WriteLine($"🧹 Cleaned up {expiredKeys.Count} expired encryption keys at {now:yyyy-MM-dd HH:mm:ss}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during key cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates that an encryption key is properly formatted
        /// </summary>
        public static bool ValidateEncryptionKey(string keyBase64)
        {
            try
            {
                if (string.IsNullOrEmpty(keyBase64))
                    return false;

                var keyBytes = Convert.FromBase64String(keyBase64);
                return keyBytes.Length == 32; // Must be 256 bits
            }
            catch
            {
                return false;
            }
        }
    }
}