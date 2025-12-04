using System.Security.Cryptography;
using System.Text;

namespace TokenRelay.Utilities;

/// <summary>
/// Helper class for encrypting debug log body content using AES encryption.
/// When enabled via the DEBUG_BODY_ENCRYPTION_KEY environment variable,
/// JSON bodies in debug logs will be encrypted for security.
/// </summary>
public static class DebugEncryptionHelper
{
    private const string EncryptionKeyEnvVar = "DEBUG_BODY_ENCRYPTION_KEY";
    private static readonly Lazy<string?> _encryptionKey = new(() =>
        Environment.GetEnvironmentVariable(EncryptionKeyEnvVar));

    /// <summary>
    /// Gets whether debug body encryption is enabled (encryption key is configured).
    /// </summary>
    public static bool IsEnabled => !string.IsNullOrEmpty(_encryptionKey.Value);

    /// <summary>
    /// Encrypts the given content if encryption is enabled.
    /// Returns the original content if encryption is not enabled or fails.
    /// </summary>
    /// <param name="content">The content to encrypt</param>
    /// <returns>Encrypted content with ENC_DEBUG: prefix, or original content if not enabled</returns>
    public static string EncryptIfEnabled(string content)
    {
        if (string.IsNullOrEmpty(content) || !IsEnabled)
        {
            return content;
        }

        return Encrypt(content, _encryptionKey.Value!);
    }

    /// <summary>
    /// Encrypts content using AES encryption.
    /// </summary>
    /// <param name="plainText">The plain text to encrypt</param>
    /// <param name="encryptionKey">The encryption key</param>
    /// <returns>Encrypted content with ENC_DEBUG: prefix</returns>
    public static string Encrypt(string plainText, string encryptionKey)
    {
        try
        {
            if (string.IsNullOrEmpty(plainText) || string.IsNullOrEmpty(encryptionKey))
            {
                return plainText;
            }

            using var aes = Aes.Create();
            var keyBytes = Encoding.UTF8.GetBytes(encryptionKey.PadRight(32).Substring(0, 32));
            aes.Key = keyBytes;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Combine IV and encrypted data
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return "ENC_DEBUG:" + Convert.ToBase64String(result);
        }
        catch
        {
            // Return original content if encryption fails
            return plainText;
        }
    }

    /// <summary>
    /// Decrypts content that was encrypted with this helper.
    /// </summary>
    /// <param name="encryptedText">The encrypted text (with ENC_DEBUG: prefix)</param>
    /// <param name="encryptionKey">The encryption key</param>
    /// <returns>Decrypted plain text</returns>
    public static string Decrypt(string encryptedText, string encryptionKey)
    {
        try
        {
            if (string.IsNullOrEmpty(encryptedText) || string.IsNullOrEmpty(encryptionKey))
            {
                return encryptedText;
            }

            if (!encryptedText.StartsWith("ENC_DEBUG:"))
            {
                return encryptedText;
            }

            var actualEncryptedData = encryptedText.Substring(10); // Remove "ENC_DEBUG:" prefix

            using var aes = Aes.Create();
            var keyBytes = Encoding.UTF8.GetBytes(encryptionKey.PadRight(32).Substring(0, 32));
            aes.Key = keyBytes;

            var encryptedBytes = Convert.FromBase64String(actualEncryptedData);

            // First 16 bytes are the IV
            var iv = new byte[16];
            var cipherText = new byte[encryptedBytes.Length - 16];
            Buffer.BlockCopy(encryptedBytes, 0, iv, 0, 16);
            Buffer.BlockCopy(encryptedBytes, 16, cipherText, 0, cipherText.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            return encryptedText;
        }
    }
}
