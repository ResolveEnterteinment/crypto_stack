namespace Application.Interfaces
{
    /// <summary>
    /// Interface for encryption operations
    /// </summary>
    public interface IEncryptionService
    {
        /// <summary>
        /// Encrypts a plain text string
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>Base64-encoded encrypted string</returns>
        string Encrypt(string plainText);

        /// <summary>
        /// Decrypts a previously encrypted string
        /// </summary>
        /// <param name="cipherText">The Base64-encoded encrypted string</param>
        /// <returns>The original plain text</returns>
        string Decrypt(string cipherText);

        /// <summary>
        /// Generates a secure hash of text (one-way encryption)
        /// </summary>
        /// <param name="text">The text to hash</param>
        /// <param name="salt">Optional salt to use for the hash</param>
        /// <returns>Base64-encoded hash string</returns>
        string Hash(string text, string salt = null);

        /// <summary>
        /// Verifies if a text matches a previously generated hash
        /// </summary>
        /// <param name="text">The text to verify</param>
        /// <param name="hash">The hash to compare against</param>
        /// <param name="salt">Optional salt used in the original hash</param>
        /// <returns>True if the text produces the same hash</returns>
        bool VerifyHash(string text, string hash, string salt = null);
    }
}
