using Infrastructure.Services.FlowEngine.Engine;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    /// <summary>
    /// Interface for flow security validation and authorization
    /// </summary>
    public interface IFlowSecurity
    {
        /// <summary>
        /// Checks if a user has a specific role
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="roleName"></param>
        /// <returns>True is the user has role, false otherwise</returns>
        Task<bool> UserHasRoleAsync(string userId, string roleName);

        /// <summary>
        /// Validates if a user has access to execute a specific flow type
        /// </summary>
        /// <param name="userId">The user ID to validate</param>
        /// <param name="flowType">The type of flow being executed</param>
        /// <returns>True if the user is authorized, false otherwise</returns>
        Task<bool> ValidateUserAccessAsync(string userId, Type flowType);

        /// <summary>
        /// Validates if a user has access to execute a specific step
        /// </summary>
        /// <param name="userId">The user ID to validate</param>
        /// <param name="stepName">The name of the step being executed</param>
        /// <returns>True if the user is authorized, false otherwise</returns>
        Task<bool> ValidateStepAccessAsync(string userId, string stepName);

        /// <summary>
        /// Validates flow data for security concerns (sensitive data, injection attacks, etc.)
        /// </summary>
        /// <param name="flow">The flow to validate</param>
        /// <returns>Security validation result</returns>
        Task<SecurityValidationResult> ValidateFlowSecurityAsync(Flow flow);

        /// <summary>
        /// Encrypts sensitive flow data if encryption is enabled
        /// </summary>
        /// <param name="data">Data to encrypt</param>
        /// <returns>Encrypted data</returns>
        Task<string> EncryptSensitiveDataAsync(object data);

        /// <summary>
        /// Decrypts sensitive flow data if encryption is enabled
        /// </summary>
        /// <param name="encryptedData">Encrypted data to decrypt</param>
        /// <param name="targetType">Target type for deserialization</param>
        /// <returns>Decrypted data</returns>
        Task<T> DecryptSensitiveDataAsync<T>(string encryptedData);
    }

    /// <summary>
    /// Result of security validation
    /// </summary>
    public class SecurityValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> SecurityWarnings { get; set; } = new();
        public List<string> SecurityErrors { get; set; } = new();
        public bool HasSensitiveData { get; set; }
        public List<string> SensitiveFields { get; set; } = new();
    }
}