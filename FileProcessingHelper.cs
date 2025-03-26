using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Test1;

namespace Test1
{
    public class FileProcessingHelper
    {
        private static string currentUser;
        public class OnlineNode
        {
            public string dnAddress { get; set; }
            public string ipAddress { get; set; }
            public int port { get; set; }
        }


        private static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] combined = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, combined, 0, first.Length);
            Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
            return combined;
        }

        private static byte[] ComputeunencryptedFileHash(List<byte[]> hashes)
        {
            if (hashes == null || hashes.Count == 0)
                return null;

            List<byte[]> currentLevel = new List<byte[]>(hashes);

            using (SHA256 sha256 = SHA256.Create())
            {
                while (currentLevel.Count > 1)
                {
                    List<byte[]> nextLevel = new List<byte[]>();

                    for (int i = 0; i < currentLevel.Count; i += 2)
                    {
                        if (i + 1 < currentLevel.Count)
                        {
                            byte[] combined = Combine(currentLevel[i], currentLevel[i + 1]);
                            byte[] parentHash = sha256.ComputeHash(combined);
                            nextLevel.Add(parentHash);
                        }
                        else
                        {
                            // Duplicate last hash if odd number
                            byte[] combined = Combine(currentLevel[i], currentLevel[i]);
                            byte[] parentHash = sha256.ComputeHash(combined);
                            nextLevel.Add(parentHash);
                        }
                    }

                    currentLevel = nextLevel;
                }

                return currentLevel[0]; // Merkle Root
            }
        }
        private static void ProcessChunks(List<byte[]> hashes, string hashFilepath)
        {

            StringBuilder sb = new StringBuilder();

            foreach (var hash in hashes)
            {
                string hashHex = BitConverter.ToString(hash).Replace("-", ""); // Convert to hex string
                sb.Append(hashHex + ";");
            }

            // Remove last ';' if necessary
            if (sb.Length > 0)
                sb.Length--;


            File.WriteAllText(hashFilepath, sb.ToString());

            Console.WriteLine($"Chunk hashes written to: {hashFilepath}");
        }

        static byte[] EncryptData(byte[] dataToEncrypt, byte[] key, byte[] iv)
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

        private static async Task SendEncryptedDataAsync(OnlineNode node, byte[] encryptedData)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    //await client.ConnectAsync(node.ipAddress, node.port);
                    await client.ConnectAsync("127.0.0.1", 5000);

                    using (NetworkStream stream = client.GetStream())
                    {
                        // Send encrypted data length first (optional, for easier parsing)
                        byte[] lengthBytes = BitConverter.GetBytes(encryptedData.Length);
                        await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);

                        // Send encrypted data
                        await stream.WriteAsync(encryptedData, 0, encryptedData.Length);
                        await stream.FlushAsync();
                    }
                }

                Console.WriteLine($"Data sent to {node.ipAddress}:{node.port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send data: {ex.Message}");
            }
        }

        private static async Task<OnlineNode> GetRandomOnlineNodeAsync()
        {
            using (HttpClient client = new HttpClient())
            {
                // Accept self-signed certs for localhost
                HttpClientHandler handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                client.BaseAddress = new Uri("https://dbserver01.azurewebsites.net");
                client.DefaultRequestHeaders.Accept.Clear();

                HttpResponseMessage response = await client.GetAsync("/api/OnlineNodes");
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Deserialize
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var nodes = JsonSerializer.Deserialize<List<OnlineNode>>(jsonResponse, options);

                if (nodes == null || nodes.Count == 0)
                    throw new Exception("No online nodes found!");

                // Select random node
                Random rand = new Random();
                int index = rand.Next(nodes.Count);

                return nodes[index];
            }
        }

        private static async Task<List<byte[]>> StoreChunks()
        {
            List<byte[]> chunksHash = new List<byte[]>();
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dnStorePath = Path.Combine(documentsPath, "DNStore");
            string userPath = Path.Combine(dnStorePath, currentUser);
            string uploadtemp = Path.Combine(userPath, "uploadData");

            string[] allFiles = Directory.GetFiles(uploadtemp);
            var tempFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith("temp"));
            foreach (var file in tempFiles)
            {
                string configPath = Path.Combine(userPath, "config");
                string secretPath = Path.Combine(configPath, "secret.txt");
                var (key, iv) = LoadAESKeyIV(secretPath);
                byte[] data = File.ReadAllBytes(file);
                byte[] encryptedData = EncryptData(data, key, iv);
                SHA256 sha256 = SHA256.Create();
                byte[] shardHash = sha256.ComputeHash(encryptedData);
                chunksHash.Add(shardHash);
                //string finalSendData = Convert.ToBase64String(encryptedData);
                OnlineNode selectedNode = await GetRandomOnlineNodeAsync();
                await SendEncryptedDataAsync(selectedNode, encryptedData);

                File.Delete(file);

            }

            return chunksHash;
        }

        public static async void ProcessFile(Windows.Storage.StorageFile file, string user)
        {
            currentUser = user;
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dnStorePath = Path.Combine(documentsPath, "DNStore");
            string userPath = Path.Combine(dnStorePath, currentUser);
            string uploadtemp = Path.Combine(userPath, "uploadData");

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
                        string tempFilePath = Path.Combine(uploadtemp, tempFileName);

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
                hashFilePath = Path.Combine(uploadtemp, hashFilePath) ;

            }


            List<byte[]> chunkHashList = await StoreChunks();

            ProcessChunks(chunkHashList, hashFilePath);

        }

    }
}
