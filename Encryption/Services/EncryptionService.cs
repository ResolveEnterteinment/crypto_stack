using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Encryption.Services
{
    /// <summary>
    /// Implements encryption and hashing services using AES for two-way encryption
    /// and HMACSHA512 for one-way hashing
    /// </summary>
    public class EncryptionService : IEncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;
        private readonly ILogger<EncryptionService> _logger;
        private const int KeySize = 32; // 256 bits
        private const int IvSize = 16;  // 128 bits

        /// <summary>
        /// Initializes a new instance of the EncryptionService
        /// </summary>
        /// <param name="configuration">Application configuration containing encryption settings</param>
        /// <param name="logger">Logger for encrytion operations</param>
        /// <exception cref="InvalidOperationException">Thrown when encryption keys are not properly configured</exception>
        public EncryptionService(IConfiguration configuration, ILogger<EncryptionService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Get encryption key and IV from secure configuration
            var encryptionKey = configuration["Encryption:Key"];
            var encryptionIv = configuration["Encryption:IV"];

            if (string.IsNullOrEmpty(encryptionKey) || string.IsNullOrEmpty(encryptionIv))
            {
                _logger.LogError("Encryption keys not properly configured. Both Key and IV must be provided.");
                throw new InvalidOperationException("Encryption keys not properly configured. Please check your configuration.");
            }

            try
            {
                _key = Convert.FromBase64String(encryptionKey);
                _iv = Convert.FromBase64String(encryptionIv);

                // Validate key and IV sizes
                if (_key.Length != KeySize)
                {
                    throw new InvalidOperationException($"Encryption key must be {KeySize * 8} bits (Base64-encoded {KeySize} bytes)");
                }

                if (_iv.Length != IvSize)
                {
                    throw new InvalidOperationException($"Encryption IV must be {IvSize * 8} bits (Base64-encoded {IvSize} bytes)");
                }
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Failed to decode encryption keys. Ensure they are valid Base64 strings.");
                throw new InvalidOperationException("Invalid encryption key format. Keys must be valid Base64 strings.", ex);
            }
        }

        /// <summary>
        /// Alternative constructor for providing key and IV directly (primarily for testing)
        /// </summary>
        public EncryptionService(byte[] key, byte[] iv, ILogger<EncryptionService> logger)
        {
            if (key == null || key.Length != KeySize)
                throw new ArgumentException($"Key must be {KeySize * 8} bits", nameof(key));

            if (iv == null || iv.Length != IvSize)
                throw new ArgumentException($"IV must be {IvSize * 8} bits", nameof(iv));

            _key = key;
            _iv = iv;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Encrypts a plain text string using AES-256 encryption
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>Base64-encoded encrypted string</returns>
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(plainText);
                }

                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during encryption");
                throw new CryptographicException("Failed to encrypt data", ex);
            }
        }

        /// <summary>
        /// Decrypts a previously encrypted string using AES-256 decryption
        /// </summary>
        /// <param name="cipherText">The Base64-encoded encrypted string</param>
        /// <returns>The original plain text</returns>
        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                using var ms = new MemoryStream(Convert.FromBase64String(cipherText));
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);

                return sr.ReadToEnd();
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Invalid ciphertext format. Input is not a valid Base64 string.");
                throw new FormatException("Invalid ciphertext format. Input is not a valid Base64 string.", ex);
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Decryption failed. The ciphertext may be corrupted or the key/IV incorrect.");
                throw new CryptographicException("Decryption failed. The ciphertext may be corrupted or the key/IV incorrect.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during decryption");
                throw new CryptographicException("Failed to decrypt data", ex);
            }
        }

        /// <summary>
        /// Generates a secure hash of text using HMACSHA512
        /// </summary>
        /// <param name="text">The text to hash</param>
        /// <param name="salt">Optional salt to use for the hash</param>
        /// <returns>Base64-encoded hash string</returns>
        public string Hash(string text, string salt = null)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            try
            {
                // Combine salt if provided
                byte[] saltBytes = string.IsNullOrEmpty(salt)
                    ? new byte[0]
                    : Encoding.UTF8.GetBytes(salt);

                byte[] textBytes = Encoding.UTF8.GetBytes(text);
                byte[] combinedBytes = new byte[textBytes.Length + saltBytes.Length];

                Buffer.BlockCopy(textBytes, 0, combinedBytes, 0, textBytes.Length);
                Buffer.BlockCopy(saltBytes, 0, combinedBytes, textBytes.Length, saltBytes.Length);

                // Create hash
                using var hmac = new HMACSHA512(_key);
                byte[] hashBytes = hmac.ComputeHash(combinedBytes);

                return Convert.ToBase64String(hashBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during hashing");
                throw new CryptographicException("Failed to generate hash", ex);
            }
        }

        /// <summary>
        /// Verifies if a text matches a previously generated hash
        /// </summary>
        /// <param name="text">The text to verify</param>
        /// <param name="hash">The hash to compare against</param>
        /// <param name="salt">Optional salt used in the original hash</param>
        /// <returns>True if the text produces the same hash</returns>
        public bool VerifyHash(string text, string hash, string salt = null)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(hash))
                return false;

            try
            {
                // Generate hash with the same parameters
                string computedHash = Hash(text, salt);

                // Time-constant comparison to prevent timing attacks
                return TimeConstantEquals(hash, computedHash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during hash verification");
                return false;
            }
        }

        /// <summary>
        /// Performs a time-constant comparison of two strings to prevent timing attacks
        /// </summary>
        private static bool TimeConstantEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }

            return result == 0;
        }

        /// <summary>
        /// Generates new random encryption keys (for initial configuration)
        /// </summary>
        /// <returns>Tuple containing Base64-encoded Key and IV</returns>
        public static (string Key, string IV) GenerateKeys()
        {
            using var aes = Aes.Create();
            aes.KeySize = KeySize * 8;
            aes.GenerateKey();
            aes.GenerateIV();

            return (
                Convert.ToBase64String(aes.Key),
                Convert.ToBase64String(aes.IV)
            );
        }
    }
}