using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LeoAICadDataClient.Utilities
{
    /// <summary>
    /// Simple encryption/decryption for Leo AI authentication files
    /// Uses AES with a randomly-generated key that's created at compile time
    /// Each build generates a unique key stored in the DLL
    /// Provides basic obfuscation - user would need to reverse engineer the DLL to extract the key
    /// </summary>
    public static class AuthFileEncryption
    {
        // AES key (256-bit = 32 bytes) and IV (128-bit = 16 bytes)
        // Generated at compile time - see GenerateEncryptionKey.ps1
        private static readonly byte[] EncryptionKey = EncryptionKeyGenerated.Key;
        private static readonly byte[] EncryptionIV = EncryptionKeyGenerated.IV;

        /// <summary>
        /// Encrypts plaintext JSON content and saves to file
        /// </summary>
        /// <param name="plaintext">JSON content to encrypt</param>
        /// <param name="outputFilePath">Path to save encrypted content</param>
        public static void EncryptToFile(string plaintext, string outputFilePath)
        {
            if (string.IsNullOrEmpty(plaintext))
            {
                throw new ArgumentNullException(nameof(plaintext), "Plaintext cannot be null or empty");
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw new ArgumentNullException(nameof(outputFilePath), "Output file path cannot be null or empty");
            }

            try
            {
                byte[] encrypted = Encrypt(plaintext);

                // Ensure directory exists
                string directory = Path.GetDirectoryName(outputFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Save encrypted bytes as Base64 string (text file)
                string base64Encrypted = Convert.ToBase64String(encrypted);
                File.WriteAllText(outputFilePath, base64Encrypted);

                Logger.Info($"Successfully encrypted auth file to: {outputFilePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to encrypt auth file: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Decrypts content from file and returns plaintext JSON
        /// </summary>
        /// <param name="inputFilePath">Path to encrypted file</param>
        /// <returns>Decrypted JSON content</returns>
        public static string DecryptFromFile(string inputFilePath)
        {
            if (string.IsNullOrEmpty(inputFilePath))
            {
                throw new ArgumentNullException(nameof(inputFilePath), "Input file path cannot be null or empty");
            }

            if (!File.Exists(inputFilePath))
            {
                throw new FileNotFoundException($"Encrypted auth file not found: {inputFilePath}");
            }

            try
            {
                // Read Base64 encoded content
                string base64Encrypted = File.ReadAllText(inputFilePath);
                byte[] encrypted = Convert.FromBase64String(base64Encrypted);

                string plaintext = Decrypt(encrypted);
                Logger.Info($"Successfully decrypted auth file from: {inputFilePath}");

                return plaintext;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to decrypt auth file: {ex.Message}");
                throw new Exception($"Failed to decrypt auth file '{inputFilePath}'. The file may be corrupted or in wrong format.", ex);
            }
        }

        /// <summary>
        /// Encrypts plaintext string to encrypted bytes using AES
        /// </summary>
        private static byte[] Encrypt(string plaintext)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = EncryptionKey;
                aes.IV = EncryptionIV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plaintext);
                        }
                        return msEncrypt.ToArray();
                    }
                }
            }
        }

        /// <summary>
        /// Decrypts encrypted bytes to plaintext string using AES
        /// </summary>
        private static string Decrypt(byte[] ciphertext)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = EncryptionKey;
                aes.IV = EncryptionIV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                using (MemoryStream msDecrypt = new MemoryStream(ciphertext))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            return srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
        }
    }
}
