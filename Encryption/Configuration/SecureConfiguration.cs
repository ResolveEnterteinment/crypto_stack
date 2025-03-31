using Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Encryption.Configuration
{
    /// <summary>
    /// Provides methods to encrypt and decrypt application configuration settings
    /// </summary>
    public static class SecureConfiguration
    {
        /// <summary>
        /// Encrypts a configuration section using our EncryptionService
        /// </summary>
        /// <param name="configSection">The configuration section to encrypt</param>
        /// <param name="sectionPath">The path to the section (e.g., "Encryption:Keys")</param>
        /// <param name="outputPath">The path to write the encrypted configuration</param>
        /// <param name="encryptionService">The encryption service to use</param>
        public static void EncryptConfigSection(
            IConfiguration configSection,
            string sectionPath,
            string outputPath,
            IEncryptionService encryptionService)
        {
            if (encryptionService == null)
                throw new ArgumentNullException(nameof(encryptionService));

            var section = configSection.GetSection(sectionPath);
            if (section == null || !section.GetChildren().Any())
            {
                throw new ArgumentException($"Section {sectionPath} not found or is empty");
            }

            var values = section.GetChildren()
                .ToDictionary(c => c.Key, c => c.Value);

            var json = System.Text.Json.JsonSerializer.Serialize(values);

            // Encrypt the JSON string
            string encryptedJson = encryptionService.Encrypt(json);

            // Write to file
            File.WriteAllText(outputPath, encryptedJson);
        }

        /// <summary>
        /// Decrypts a previously encrypted configuration file
        /// </summary>
        /// <param name="encryptedFilePath">Path to the encrypted configuration file</param>
        /// <param name="encryptionService">The encryption service to use</param>
        /// <returns>Dictionary of decrypted configuration values</returns>
        public static Dictionary<string, string> DecryptConfigFile(
            string encryptedFilePath,
            IEncryptionService encryptionService)
        {
            if (encryptionService == null)
                throw new ArgumentNullException(nameof(encryptionService));

            if (!File.Exists(encryptedFilePath))
            {
                throw new FileNotFoundException($"Encrypted config file not found: {encryptedFilePath}");
            }

            string encryptedContent = File.ReadAllText(encryptedFilePath);

            // Decrypt the content
            string decryptedJson = encryptionService.Decrypt(encryptedContent);

            // Deserialize the JSON
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(decryptedJson);
        }
    }
}