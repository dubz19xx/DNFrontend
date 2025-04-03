using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Test1.Utilities
{
    internal class CryptHelper
    {
        public static string GenerateMasterKey(int byteLength)
        {
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256; // AES-256
                aes.GenerateKey();
                aes.GenerateIV();

                // Convert key and IV to Base64 strings
                string keyBase64 = Convert.ToBase64String(aes.Key);
                string ivBase64 = Convert.ToBase64String(aes.IV);

                // Save to file as: key;iv
                string content = $"{keyBase64};{ivBase64}";
                return content;
            }
        }

        public static byte[] EncryptData(byte[] dataToEncrypt, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (MemoryStream msEncrypt = new MemoryStream())
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    csEncrypt.Write(dataToEncrypt, 0, dataToEncrypt.Length);
                    csEncrypt.FlushFinalBlock();

                    return msEncrypt.ToArray(); // Encrypted data
                }
            }
        }


    }
}
