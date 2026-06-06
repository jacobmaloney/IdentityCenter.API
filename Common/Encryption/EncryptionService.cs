using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;

namespace Common.Encryption
{
    public class EncryptionService : IEncryptionService
    {
        private readonly IDataProtector _protector;

        public EncryptionService(IDataProtectionProvider dataProtectionProvider)
        {
            _protector = dataProtectionProvider.CreateProtector("IdentityCenter.Encryption");
        }

        public async Task<string> EncryptAsync(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            return await Task.Run(() =>
            {
                try
                {
                    // Use Data Protection API which handles key persistence automatically
                    return _protector.Protect(plainText);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Encryption failed", ex);
                }
            });
        }

        public async Task<string> DecryptAsync(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            // If the value is already plain JSON (e.g. HR CSV connections store "{}" credentials),
            // return it as-is rather than attempting decryption
            var trimmed = cipherText.TrimStart();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                return cipherText;

            return await Task.Run(() =>
            {
                try
                {
                    // Use Data Protection API which handles key persistence automatically
                    return _protector.Unprotect(cipherText);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Decryption failed", ex);
                }
            });
        }

        public async Task<byte[]> EncryptBytesAsync(byte[] plainBytes)
        {
            if (plainBytes == null || plainBytes.Length == 0)
                return Array.Empty<byte>();

            return await Task.Run(() =>
            {
                try
                {
                    // Convert to base64 string, protect, then convert back to bytes
                    var plainText = Convert.ToBase64String(plainBytes);
                    var protectedText = _protector.Protect(plainText);
                    return Encoding.UTF8.GetBytes(protectedText);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Encryption failed", ex);
                }
            });
        }

        public async Task<byte[]> DecryptBytesAsync(byte[] cipherBytes)
        {
            if (cipherBytes == null || cipherBytes.Length == 0)
                return Array.Empty<byte>();

            return await Task.Run(() =>
            {
                try
                {
                    // Convert from bytes, unprotect, then convert from base64
                    var protectedText = Encoding.UTF8.GetString(cipherBytes);
                    var plainText = _protector.Unprotect(protectedText);
                    return Convert.FromBase64String(plainText);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Decryption failed", ex);
                }
            });
        }

        public string GenerateKey()
        {
            var keyBytes = GenerateRandomBytes(32);
            return Convert.ToBase64String(keyBytes);
        }

        public bool ValidateKey(string key)
        {
            try
            {
                var keyBytes = Convert.FromBase64String(key);
                return keyBytes.Length == 32;
            }
            catch
            {
                return false;
            }
        }

        public byte[] EncryptBytes(byte[] plainBytes, string password)
        {
            if (plainBytes == null || plainBytes.Length == 0)
                throw new ArgumentException("Data to encrypt cannot be empty", nameof(plainBytes));

            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be empty", nameof(password));

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Derive key from password using PBKDF2
            var salt = GenerateRandomBytes(32);
            using var deriveBytes = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            aes.Key = deriveBytes.GetBytes(32);
            aes.IV = GenerateRandomBytes(16);

            using var ms = new MemoryStream();
            // Write salt and IV at the beginning
            ms.Write(salt, 0, salt.Length);
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
                cs.FlushFinalBlock();
            }

            return ms.ToArray();
        }

        public byte[] DecryptBytes(byte[] cipherBytes, string password)
        {
            if (cipherBytes == null || cipherBytes.Length == 0)
                throw new ArgumentException("Data to decrypt cannot be empty", nameof(cipherBytes));

            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be empty", nameof(password));

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Extract salt and IV from the beginning
            var salt = new byte[32];
            var iv = new byte[16];
            Array.Copy(cipherBytes, 0, salt, 0, 32);
            Array.Copy(cipherBytes, 32, iv, 0, 16);

            // Derive key from password using same PBKDF2 settings
            using var deriveBytes = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            aes.Key = deriveBytes.GetBytes(32);
            aes.IV = iv;

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(cipherBytes, 48, cipherBytes.Length - 48); // Skip salt (32) + IV (16) = 48 bytes
                cs.FlushFinalBlock();
            }

            return ms.ToArray();
        }

        private static byte[] GenerateRandomBytes(int length)
        {
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }
    }
}