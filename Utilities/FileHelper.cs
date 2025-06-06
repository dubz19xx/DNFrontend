﻿using Newtonsoft.Json;
using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Security.Cryptography;

using System.Text;

using System.Threading.Tasks;

using Test1.Models;

using Windows.Data.Text;

using Test1.Services;
using System.Net.Http;



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

        public static string downloadPath;

        public static string blockchainPath;

        public FileHelper(string username)

        {

            documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            dnStorePath = Path.Combine(documentsPath, "DNStore");

            userFolderPath = Path.Combine(dnStorePath, username);

            mainstoragePath = Path.Combine(userFolderPath, "storage");

            uploadqueuePath = Path.Combine(userFolderPath, "uploadData");

            configPath = Path.Combine(userFolderPath, "config");

            downloadPath = Path.Combine(userFolderPath, "downloads");

            blockchainPath = Path.Combine(userFolderPath, "blockchain.json");
        }



        // Modified to only create config folder during registration

        public void SetupRegistrationFolders()

        {

            if (!Directory.Exists(dnStorePath))

                Directory.CreateDirectory(dnStorePath);



            if (!Directory.Exists(userFolderPath))

                Directory.CreateDirectory(userFolderPath);



            // Only create config folder during registration

            if (!Directory.Exists(configPath))

                Directory.CreateDirectory(configPath);

            if(!Directory.Exists(downloadPath))
                Directory.CreateDirectory(downloadPath);

        }



        // New method for login-time folder setup

        public void SetupLoginFolders()

        {

            // Verify master key exists first

            string secretFile = Path.Combine(configPath, "secret.txt");

            if (!File.Exists(secretFile))

                throw new InvalidOperationException("Master key not found - invalid user folder");



            // Create remaining folders

            if (!Directory.Exists(mainstoragePath))

                Directory.CreateDirectory(mainstoragePath);



            if (!Directory.Exists(uploadqueuePath))

                Directory.CreateDirectory(uploadqueuePath);

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


            ProcessChunks(chunkHashList, hashFilePath);



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

                OnlineNode selectedNode = await Blockchain.SelectBestNode();

                List<OnlineNode> tempList = new List<OnlineNode>();

                tempList.Add(selectedNode);



                // Create the binary message with prefix
                byte[] prefixBytes = Encoding.UTF8.GetBytes("SAVESHARD|");
                byte[] message = new byte[prefixBytes.Length + encryptedData.Length];
                Buffer.BlockCopy(prefixBytes, 0, message, 0, prefixBytes.Length);
                Buffer.BlockCopy(encryptedData, 0, message, prefixBytes.Length, encryptedData.Length);

                await Blockchain.p2pService.PunchPeers(tempList);

                // Send via P2PService
                await Blockchain.p2pService.SendUDPmsg(
                    selectedNode.ipAddress,
                    selectedNode.port,
                    message);  // Note: Changed to accept raw bytes




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



        public static async void SaveShardLocally(byte[] shardData)
        {
            try
            {
                // Generate hash-based filename
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(shardData);
                    string shardName = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                    string shardPath = Path.Combine(mainstoragePath, shardName + ".shard");

                    // Ensure directory exists
                    //Directory.CreateDirectory(mainstoragePath);

                    // Write binary data directly
                    File.WriteAllBytes(shardPath, shardData);

                    //create transaction
                    StorageCommitmentTransaction transaction = new StorageCommitmentTransaction();
                    transaction.NodeId = MainAppPage.address;
                    transaction.Timestamp = DateTime.Now;
                    transaction.ChunkHash = shardName;
                    transaction.TransactionType = "STORAGE";

                    Blockchain.AddTransaction(transaction);

                    //broadcast transaction
                    string transitTransaction = JsonConvert.SerializeObject(transaction);

                    List<OnlineNode> onlineNodes = await Blockchain.GetOnlineNodes();
                    Blockchain.p2pService.PunchPeers(onlineNodes);

                    foreach (OnlineNode onlineNode in onlineNodes) {
                        await Blockchain.p2pService.SendUDPmsg(onlineNode.ipAddress, onlineNode.port, "ADDTRANSACTION|", transitTransaction);
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving shard: {ex.Message}");
                throw;
            }
        }

        public static async Task DownloadFile()
        {
            try
            {
                // Get all files in the upload queue directory
                string[] filesInQueue = Directory.GetFiles(uploadqueuePath);

                // Filter out non-hashlist files (assuming they start with "fhl.")
                var hashListFiles = filesInQueue.Where(f => Path.GetFileName(f).StartsWith("fhl.")).ToList();

                if (hashListFiles.Count == 0)
                {
                    Console.WriteLine("No downloadable files found in upload queue");
                    
                }

                // Select a random file
                Random random = new Random();
                string selectedFile = hashListFiles[random.Next(hashListFiles.Count)];

                Console.WriteLine($"Selected file for download: {Path.GetFileName(selectedFile)}");

                // Read the file content
                string fileContent = await File.ReadAllTextAsync(selectedFile);

                // Split the content by semicolons
                List<string> chunkHashes = fileContent.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                // Dictionary to map chunkHash to node addresses
                Dictionary<string, string> chunkLocationMapping = new Dictionary<string, string>();

                // Get the blockchain
                List<Block> blockchain = await Blockchain.GetBlockchain();

                // Search each block's transactions for the chunk hashes
                foreach (string chunkHash in chunkHashes)
                {
                    bool found = false;

                    foreach (Block block in blockchain)
                    {
                        foreach (StorageCommitmentTransaction transaction in block.Transactions)
                        {
                            if (transaction.ChunkHash.Equals(chunkHash, StringComparison.OrdinalIgnoreCase))
                            {
                                // Found the chunk, map the chunkHash to the node address
                                chunkLocationMapping[chunkHash] = transaction.NodeId;
                                found = true;
                                break;
                            }
                        }

                        if (found) break;
                    }


                }

                Dictionary<string, OnlineNode> onlineNodeMapping = new Dictionary<string, OnlineNode>();

                // Fetch the OnlineNode details for each dnAddress (from chunkLocationMapping)
                foreach (var chunkEntry in chunkLocationMapping)
                {
                    string dnAddress = chunkEntry.Value;


                    // Send a GET request to fetch the OnlineNode details for this address
                    HttpResponseMessage response = await NetworkService.SendGetRequest($"OnlineNodes/{dnAddress}");

                    if (response.IsSuccessStatusCode)
                    {
                        // Deserialize the response into an OnlineNode
                        string responseContent = await response.Content.ReadAsStringAsync();
                        OnlineNode onlineNode = JsonConvert.DeserializeObject<OnlineNode>(responseContent);

                        // Add the OnlineNode to the dictionary
                        onlineNodeMapping[dnAddress] = onlineNode;
                    }

                }

                foreach (var chunkEntry in chunkLocationMapping)
                {
                    string chunkHash = chunkEntry.Key;
                    string dnAddress = chunkEntry.Value;

                    // Check if the onlineNodeMapping contains the dnAddress
                    if (onlineNodeMapping.ContainsKey(dnAddress))
                    {
                        OnlineNode onlineNode = onlineNodeMapping[dnAddress];

                        // Send the UDP message with the shardHash (which is the chunkHash)
                        Console.WriteLine($"Sending UDP message to {onlineNode.dnAddress} at {onlineNode.ipAddress}:{onlineNode.port} with shardHash: {chunkHash}");
                        await Blockchain.p2pService.SendUDPmsg(onlineNode.ipAddress, onlineNode.port, "DOWNLOADSHARD|", chunkHash);
                    }
                }

            }

            catch (Exception ex)
            {
                Console.WriteLine($"Error in DownloadFile: {ex.Message}");
                throw;
            }
        }

        public static byte[] RetrieveShards(string hash)
        {
            string shardPath = Path.Combine(mainstoragePath, hash);
            shardPath = shardPath + ".shard";
            return File.ReadAllBytes(shardPath);

        }

        public static void StoreShard(byte[] shardData)
        {
            byte[] tempShardName = SHA256.Create().ComputeHash(shardData);
            string destination = Path.Combine(downloadPath, Encoding.UTF8.GetString(tempShardName));
            File.WriteAllBytes(destination, shardData);
        }

    }

}
