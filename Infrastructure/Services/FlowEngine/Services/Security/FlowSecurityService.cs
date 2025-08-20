using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Services.FlowEngine.Services.Security
{
    /// <summary>
    /// Implementation of flow security service
    /// </summary>
    public class FlowSecurityService : IFlowSecurity
    {
        private readonly ILogger<FlowSecurityService> _logger;
        private static readonly Regex SqlInjectionPattern = new(@"(\b(ALTER|CREATE|DELETE|DROP|EXEC(UTE)?|INSERT( +INTO)?|MERGE|SELECT|UPDATE|UNION( +ALL)?)\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex XssPattern = new(@"<[^>]*>|javascript:|vbscript:|onload|onerror|onclick",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public FlowSecurityService(ILogger<FlowSecurityService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ValidateUserAccessAsync(string userId, Type flowType)
        {
            try
            {
                // Basic validation
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User access validation failed: empty user ID");
                    return false;
                }

                if (userId == "system")
                {
                    _logger.LogInformation("System user access granted for flow type {FlowType}", flowType.Name);
                    return true;
                }

                // Check for flow-specific authorization attributes
                var authAttributes = flowType.GetCustomAttributes(typeof(AuthorizeFlowAttribute), true);
                if (authAttributes.Any())
                {
                    foreach (AuthorizeFlowAttribute attr in authAttributes)
                    {
                        if (!await ValidateFlowPermission(userId, attr.RequiredPermission))
                        {
                            _logger.LogWarning("User {UserId} lacks required permission {Permission} for flow {FlowType}",
                                userId, attr.RequiredPermission, flowType.Name);
                            return false;
                        }
                    }
                }

                // Default behavior: allow access for authenticated users
                _logger.LogInformation("User access granted for {UserId} to flow type {FlowType}", userId, flowType.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating user access for {UserId} to flow type {FlowType}", userId, flowType.Name);
                return false;
            }
        }

        public async Task<bool> ValidateStepAccessAsync(string userId, string stepName)
        {
            try
            {
                // Basic validation
                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stepName))
                {
                    _logger.LogWarning("Step access validation failed: empty user ID or step name");
                    return false;
                }

                if (userId == "system")
                {
                    return true;
                }

                // Check for sensitive step patterns
                var sensitiveSteps = new[] { "delete", "remove", "purge", "admin", "sudo", "elevate" };
                if (sensitiveSteps.Any(pattern => stepName.ToLower().Contains(pattern)))
                {
                    if (!await ValidateAdminAccess(userId))
                    {
                        _logger.LogWarning("User {UserId} attempted to access sensitive step {StepName} without admin privileges",
                            userId, stepName);
                        return false;
                    }
                }

                _logger.LogDebug("Step access granted for {UserId} to step {StepName}", userId, stepName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating step access for {UserId} to step {StepName}", userId, stepName);
                return false;
            }
        }

        public async Task<SecurityValidationResult> ValidateFlowSecurityAsync(FlowDefinition flow)
        {
            var result = new SecurityValidationResult { IsValid = true };

            try
            {
                // Validate flow data for security issues
                if (flow.Data != null)
                {
                    var dataJson = JsonSerializer.Serialize(flow.Data);

                    // Check for SQL injection patterns
                    if (SqlInjectionPattern.IsMatch(dataJson))
                    {
                        result.SecurityWarnings.Add("Potential SQL injection pattern detected in flow data");
                    }

                    // Check for XSS patterns
                    if (XssPattern.IsMatch(dataJson))
                    {
                        result.SecurityWarnings.Add("Potential XSS pattern detected in flow data");
                    }

                    // Check for sensitive data
                    var sensitiveFields = ExtractSensitiveFields(flow.Data);
                    if (sensitiveFields.Any())
                    {
                        result.HasSensitiveData = true;
                        result.SensitiveFields.AddRange(sensitiveFields);
                        result.SecurityWarnings.Add($"Sensitive data detected in fields: {string.Join(", ", sensitiveFields)}");
                    }
                }

                // Validate user context
                if (string.IsNullOrEmpty(flow.UserId))
                {
                    result.SecurityErrors.Add("Flow must have a valid user context");
                    result.IsValid = false;
                }

                // Validate correlation ID for audit trail
                if (string.IsNullOrEmpty(flow.CorrelationId))
                {
                    result.SecurityWarnings.Add("Flow should have a correlation ID for audit purposes");
                }

                result.IsValid = !result.SecurityErrors.Any();

                _logger.LogInformation(
                    "Security validation completed for flow {FlowId}. Valid: {IsValid}, Errors: {ErrorCount}, Warnings: {WarningCount}, HasSensitiveData: {HasSensitiveData}",
                    flow.FlowId, result.IsValid, result.SecurityErrors.Count, result.SecurityWarnings.Count, result.HasSensitiveData);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during security validation for flow {FlowId}", flow.FlowId);
                result.SecurityErrors.Add($"Security validation error: {ex.Message}");
                result.IsValid = false;
                return result;
            }
        }

        public async Task<string> EncryptSensitiveDataAsync(object data)
        {
            try
            {
                // This is a placeholder implementation
                // In a real implementation, you would use proper encryption
                var json = JsonSerializer.Serialize(data);
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                var base64 = Convert.ToBase64String(bytes);

                _logger.LogDebug("Sensitive data encrypted");
                return base64;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encrypting sensitive data");
                throw;
            }
        }

        public async Task<T> DecryptSensitiveDataAsync<T>(string encryptedData)
        {
            try
            {
                // This is a placeholder implementation
                // In a real implementation, you would use proper decryption
                var bytes = Convert.FromBase64String(encryptedData);
                var json = System.Text.Encoding.UTF8.GetString(bytes);
                var result = JsonSerializer.Deserialize<T>(json);

                _logger.LogDebug("Sensitive data decrypted");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decrypting sensitive data");
                throw;
            }
        }

        private async Task<bool> ValidateFlowPermission(string userId, string requiredPermission)
        {
            // This would typically check against your authorization system
            // For now, we'll implement basic role checking
            try
            {
                // Placeholder logic - replace with your actual permission checking
                var userRoles = await GetUserRoles(userId);

                // Admin users can access everything
                if (userRoles.Contains("ADMIN"))
                    return true;

                // Check specific permission
                return userRoles.Contains(requiredPermission.ToUpper());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating flow permission for user {UserId}", userId);
                return false;
            }
        }

        private async Task<bool> ValidateAdminAccess(string userId)
        {
            try
            {
                var userRoles = await GetUserRoles(userId);
                return userRoles.Contains("ADMIN") || userRoles.Contains("SUPER_ADMIN");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating admin access for user {UserId}", userId);
                return false;
            }
        }

        private async Task<List<string>> GetUserRoles(string userId)
        {
            // This is a placeholder implementation
            // In a real implementation, you would query your user/role system
            await Task.Delay(1); // Placeholder for async operation

            // For now, return basic roles based on user ID patterns
            if (userId.Contains("admin"))
                return new List<string> { "ADMIN", "USER" };

            return new List<string> { "USER" };
        }

        private List<string> ExtractSensitiveFields(object data)
        {
            var sensitiveFields = new List<string>();
            var sensitivePatterns = new[] { "password", "secret", "key", "token", "ssn", "credit", "card", "account" };

            try
            {
                if (data is Dictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                    {
                        var fieldName = kvp.Key.ToLower();
                        if (sensitivePatterns.Any(pattern => fieldName.Contains(pattern)))
                        {
                            sensitiveFields.Add(kvp.Key);
                        }
                    }
                }
                else
                {
                    // Use reflection to check property names
                    var properties = data.GetType().GetProperties();
                    foreach (var prop in properties)
                    {
                        var propName = prop.Name.ToLower();
                        if (sensitivePatterns.Any(pattern => propName.Contains(pattern)))
                        {
                            sensitiveFields.Add(prop.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting sensitive fields from data");
            }

            return sensitiveFields;
        }
    }

    /// <summary>
    /// Attribute to specify required permissions for flow access
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class AuthorizeFlowAttribute : Attribute
    {
        public string RequiredPermission { get; }

        public AuthorizeFlowAttribute(string requiredPermission)
        {
            RequiredPermission = requiredPermission;
        }
    }
}