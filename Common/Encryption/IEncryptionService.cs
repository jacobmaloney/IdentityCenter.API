using System;
using System.Threading.Tasks;

namespace Common.Encryption
{
    public interface IEncryptionService
    {
        Task<string> EncryptAsync(string plainText);
        Task<string> DecryptAsync(string cipherText);
        Task<byte[]> EncryptBytesAsync(byte[] plainBytes);
        Task<byte[]> DecryptBytesAsync(byte[] cipherBytes);
        byte[] EncryptBytes(byte[] plainBytes, string password);
        byte[] DecryptBytes(byte[] cipherBytes, string password);
        string GenerateKey();
        bool ValidateKey(string key);
    }
}