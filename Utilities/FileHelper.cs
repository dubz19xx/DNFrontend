using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Test1.Utilities
{
    internal class FileHelper
    {
        public static string userFolderPath;
        public static string documentsPath;
        public static string dnStorePath;
        public static string mainstoragePath;
        public static string uploadqueuePath;
        public static string configPath;

        public FileHelper(string username)
        {
            documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            dnStorePath = Path.Combine(documentsPath, "DNStore");
            userFolderPath = Path.Combine(dnStorePath, username);
            mainstoragePath = Path.Combine(userFolderPath, "storage");
            uploadqueuePath = Path.Combine(userFolderPath, "uploadData");
            configPath = Path.Combine(userFolderPath, "config");
        }

        public void SetupFolders()
        {
            if (!Directory.Exists(dnStorePath))
                Directory.CreateDirectory(dnStorePath);

            if (!Directory.Exists(userFolderPath))
                Directory.CreateDirectory(userFolderPath);
   
            if (!Directory.Exists(mainstoragePath))
                Directory.CreateDirectory(mainstoragePath);
   
            if (!Directory.Exists(uploadqueuePath))
                Directory.CreateDirectory(uploadqueuePath);

            if (!Directory.Exists(configPath))
                Directory.CreateDirectory(configPath);
        }

        public void SaveMasterKey(string mKey)
        {
            string secretfile = Path.Combine(configPath, "secret.txt");
            File.WriteAllText(secretfile, mKey);
        }

        public static async Task UploadFile(Windows.Storage.StorageFile file)
        {
            const int chunkSize = 256 * 1024; // 256 KB
            byte[] buffer = new byte[chunkSize];

            var filepath = file.Path;
            string hashFilePath;
            using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                int bytesRead;
                int partNumber = 1;
                using (SHA256 sha256 = SHA256.Create())
                {
                    while ((bytesRead = fs.Read(buffer, 0, chunkSize)) > 0)
                    {
                        string tempFileName = $"temp{partNumber}";
                        string tempFilePath = Path.Combine(uploadqueuePath, tempFileName);

                        using (FileStream chunkStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                        {
                            chunkStream.Write(buffer, 0, bytesRead);
                        }

                        partNumber++;
                    }

                }

                // Read the file into a byte array
                byte[] forhashbuf = new byte[fs.Length];
                fs.Read(forhashbuf, 0, forhashbuf.Length);
                SHA256 sHA256 = SHA256.Create();
                byte[] unenchash = sHA256.ComputeHash(forhashbuf);
                StringBuilder hashStringBuilder = new StringBuilder();
                foreach (byte b in unenchash)
                {
                    hashStringBuilder.Append(b.ToString("x2")); // Convert each byte to a two-digit hexadecimal value
                }

                // Create the filename using the hash
                hashFilePath = $"fhl.{file.Name.ToString()}.{hashStringBuilder.ToString()}.dn";
                hashFilePath = Path.Combine(uploadqueuePath, hashFilePath);

            }

            List<byte[]> chunkHashList = await StoreChunks();

            //[!] TODO: write chunk hash to fhl file
            //ProcessChunks(chunkHashList, hashFilePath);

        }

        private static async Task<List<byte[]>> StoreChunks()
        {
            List<byte[]> chunksHash = new List<byte[]>();

            string[] allFiles = Directory.GetFiles(uploadqueuePath);
            var tempFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith("temp"));
            foreach (var file in tempFiles)
            {
                string secretPath = Path.Combine(configPath, "secret.txt");
                var (key, iv) = LoadAESKeyIV(secretPath);
                byte[] data = File.ReadAllBytes(file);
                byte[] encryptedData = CryptHelper.EncryptData(data, key, iv);
                SHA256 sha256 = SHA256.Create();
                byte[] shardHash = sha256.ComputeHash(encryptedData);
                chunksHash.Add(shardHash);
                //string finalSendData = Convert.ToBase64String(encryptedData);
                

                //[!] TODO: store each chunk at random node

                //OnlineNode selectedNode = await GetRandomOnlineNodeAsync();
                //await SendEncryptedDataAsync(selectedNode, encryptedData);

                File.Delete(file);

            }

            return chunksHash;
        }

        static (byte[] key, byte[] iv) LoadAESKeyIV(string filePath)
        {
            string content = File.ReadAllText(filePath);
            string[] parts = content.Split(';');

            if (parts.Length != 2)
                throw new Exception("Invalid key file format!");

            byte[] key = Convert.FromBase64String(parts[0]);
            byte[] iv = Convert.FromBase64String(parts[1]);

            return (key, iv);
        }
    }
}
