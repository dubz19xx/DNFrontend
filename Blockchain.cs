using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Transactions;
using System.Net.Http;
using Newtonsoft.Json;
using System.ComponentModel;
using Windows.Storage;
using Windows.Media.Protection.PlayReady;

namespace Test1
{
    public class StorageCommitmentTransaction
    {
        public string TransactionType { get; set; }
        public string FileId { get; set; }
        public string ChunkHash { get; set; }
        public string NodeId { get; set; }
        public DateTime Timestamp { get; set; }

        public string CalculateHash()
        {
            string rawData = $"{TransactionType}-{FileId}-{ChunkHash}-{NodeId}-{Timestamp}";
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                return Convert.ToHexString(bytes);
            }
        }
    }


    public class Block
    {
        private string selfAddy;
        public int Index { get; set; }
        public DateTime Timestamp { get; set; }
        public string PreviousHash { get; set; }
        public List<StorageCommitmentTransaction> Transactions { get; set; }
        public string MerkleRoot { get; set; }
        public string BlockHash { get; set; }

        // Reference to the previous block (for linking)
        public Block PreviousBlock { get; set; }

        public Block()
        {
            Transactions = new List<StorageCommitmentTransaction>();
        }

        public void CalculateMerkleRoot()
        {
            List<string> hashes = Transactions.Select(tx => tx.CalculateHash()).ToList();

            while (hashes.Count > 1)
            {
                List<string> newHashes = new List<string>();

                for (int i = 0; i < hashes.Count; i += 2)
                {
                    if (i + 1 < hashes.Count)
                    {
                        string combined = hashes[i] + hashes[i + 1];
                        using (SHA256 sha256 = SHA256.Create())
                        {
                            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                            newHashes.Add(Convert.ToHexString(bytes));
                        }
                    }
                    else
                    {
                        // Odd one out, hash it alone
                        newHashes.Add(hashes[i]);
                    }
                }

                hashes = newHashes;
            }

            MerkleRoot = hashes.Count > 0 ? hashes[0] : string.Empty;
        }

        public void CalculateBlockHash()
        {
            string rawData = $"{Index}-{Timestamp}-{PreviousHash}-{MerkleRoot}";
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                BlockHash = Convert.ToHexString(bytes);
            }
        }
    }

    public class Blockchain
    {
        public static List<Block> blockchainCopy = new List<Block>();
        public static Block GenesisBlock { get; private set; }
        public static Block LatestBlock { get; private set; }

        static List<StorageCommitmentTransaction> pendingTransactions = new List<StorageCommitmentTransaction>();

        public Blockchain()
        {
            // Create the genesis block (first block in the chain)
            GenesisBlock = CreateGenesisBlock();
            LatestBlock = GenesisBlock;
            blockchainCopy.Add(LatestBlock);

        }

        public async Task InitializeBC(string uname, string addy)
        {
            await DownloadBlocks();
            await ListenerThread(uname, addy);
        }


        public Block CreateGenesisBlock()
        {
            // Genesis block doesn't have a previous block, so previous hash is empty
            return new Block
            {
                Index = 0,
                Timestamp = DateTime.Now,
                PreviousHash = string.Empty,
                Transactions = new List<StorageCommitmentTransaction>(), // You can add genesis transactions here
            };
        }

        public void CreateNewBlock(List<StorageCommitmentTransaction> transactions)
        {
            // Create a new block
            Block newBlock = new Block
            {
                Index = LatestBlock.Index + 1,
                Timestamp = DateTime.Now,
                PreviousBlock = LatestBlock,
                PreviousHash = LatestBlock.BlockHash,
                Transactions = transactions
            };

            // Calculate Merkle root and block hash for the new block
            newBlock.CalculateMerkleRoot();
            newBlock.CalculateBlockHash();

            // Set the latest block to be the new block
            LatestBlock = newBlock;

            blockchainCopy.Add(newBlock);
        }

        public class OnlineNode
        {
            public string dnAddress { get; set; }
            public string ipAddress { get; set; }
            public int port { get; set; }
        }

        // Retrieve the globally stored blockchain
        public static List<Block> GetBlockchain()
        {
            return blockchainCopy;
        }

        public static async Task<List<OnlineNode>> GetOnlineNodes()
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

                List<OnlineNode> nodes = JsonConvert.DeserializeObject<List<OnlineNode>>(jsonResponse);

                return nodes;

            }

        }

        public static async Task<string> ReceiveBlockchainResponse(UdpClient udpClient)
        {
            // Listen for the response from the node
            var result = await udpClient.ReceiveAsync();

            // The result contains the response data and remote endpoint information
            string responseData = Encoding.UTF8.GetString(result.Buffer);

            return responseData;
        }

        private static async Task SendUdpRequest(OnlineNode node, string message)
        {
            using (UdpClient udpClient = new UdpClient())
            {
                byte[] requestMessage = Encoding.UTF8.GetBytes(message);

                // Send request to the node's IP and port
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse(node.ipAddress), node.port);
                int xyz = udpClient.Send(requestMessage, requestMessage.Length, remoteEndPoint);

                // Listen for the response (receive the blockchain)
                var responseData = udpClient.Receive(ref remoteEndPoint);

                Console.WriteLine(responseData);
                // Process the received blockchain response (this will update the blockchain)
                //UpdateBlockchainFromJson(responseData);
            }
        }

        public static async Task DownloadBlocks()
        {
            List<OnlineNode> onlineNodes = await GetOnlineNodes();
            if (onlineNodes.Count > 1)
            {

                foreach (var node in onlineNodes)
                {
                    await SendUdpRequest(node, "REQUEST_BLOCKCHAIN");
                }

            }
        }

        public static async Task SaveShardData(byte[] shard, string storageFolder)
        {
            // Compute SHA256 hash
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(shard);
            }

            string hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();

            var transaction = new StorageCommitmentTransaction
            {
                FileId = Guid.NewGuid().ToString(),
                ChunkHash = hashString,
                NodeId = "Node01",
                Timestamp = DateTime.UtcNow
            };

            pendingTransactions.Add(transaction);

            // Save to file
            string filePath = Path.Combine(storageFolder, hashString + ".bin");
            File.WriteAllBytes(filePath, shard);

            Console.WriteLine($"Data saved to {filePath}");
            Console.WriteLine($"Transaction added: {transaction.CalculateHash()}");

            // Sync transaction
            SyncData("Transaction", transaction);


        }

        public static async void SyncData(string type, object data)
        {

            List<OnlineNode> onlinenodes = await GetOnlineNodes();

            foreach (OnlineNode node in onlinenodes) {

                string syncdata = "UPDATE_TRANSACTION\n" + data.ToString();
                using (UdpClient udpClient = new UdpClient())
                {
                    // Prepare the message to send
                    byte[] messageBytes = Encoding.UTF8.GetBytes(syncdata);

                    // Create the endpoint using the node's IP address and port
                    IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(node.ipAddress), node.port);

                    // Send the message asynchronously to the node using UDP
                    await udpClient.SendAsync(messageBytes, messageBytes.Length, endPoint);

                    Console.WriteLine($"Successfully sent transaction to node {node.dnAddress} ({node.ipAddress}:{node.port})");
                }
            }
        }


        public static async Task ListenerThread(string uname, string selfaddy)
        {
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dnStorePath = Path.Combine(documentsPath, "DNStore");
            string userFolderPath = Path.Combine(dnStorePath, uname);
            string storageFolder = Path.Combine(userFolderPath, "storage");

            if (!Directory.Exists(storageFolder))
            {
                Directory.CreateDirectory(storageFolder);
            }

            HttpClient client = new HttpClient();
            
            // Accept self-signed certs for localhost
            HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            client.BaseAddress = new Uri("https://dbserver01.azurewebsites.net");
            client.DefaultRequestHeaders.Accept.Clear();
            string getportstring = "/api/OnlineNodes/" + selfaddy;

            HttpResponseMessage response = await client.GetAsync(getportstring);
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync();

            OnlineNode selfnode = JsonConvert.DeserializeObject<OnlineNode>(jsonResponse);
            int punchedPort = selfnode.port;

            string localIp = "0.0.0.0"; 

            using (UdpClient udpListener = new UdpClient(punchedPort))
            {
                Console.WriteLine($"Listening for incoming requests on port ...");
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(localIp), punchedPort);
                while (true)
                {
                    try
                    {
                        // Wait for an incoming request
                        byte[] receivedData = udpListener.Receive(ref localEndPoint);

                        string requestMessage = Encoding.UTF8.GetString(receivedData);
                        string[] parts = requestMessage.Split(new[] { '\n' }, 2, StringSplitOptions.None);
                        //Console.WriteLine($"Received request: {requestMessage} from {result.RemoteEndPoint}");
                        string command = parts[0];
                        byte[] sharedData = Encoding.UTF8.GetBytes(parts[1]);
                        // Process the request
                        if (command == "REQUEST_BLOCKCHAIN")
                        {
                            var blockchainData = GetBlockchain();

                            // Create the JSON object with the blockchain and the latest block index
                            var blockchainResponse = new
                            {
                                blocks = blockchainData,
                                latestBlockIndex = blockchainData.LastOrDefault()?.Index ?? -1
                            };

                            // Serialize the blockchain to JSON and then to byte array
                            string blockchainJson = JsonConvert.SerializeObject(blockchainResponse);
                            byte[] responseData = Encoding.UTF8.GetBytes(blockchainJson);

                            // Send the blockchain data as a response
                            //await udpListener.SendAsync(responseData, responseData.Length, result.RemoteEndPoint);

                            //Console.WriteLine($"Sent blockchain data to {result.RemoteEndPoint}");
                        }

                        else if (command == "STORE_SHARD")
                        {
                            //store shard
                            await SaveShardData(sharedData, storageFolder);

                            // Respond to acknowledge that the shard has been stored
                            byte[] responseMessage = Encoding.UTF8.GetBytes("SHARD_RECEIVED");
                            //await udpListener.SendAsync(responseMessage, responseMessage.Length, result.RemoteEndPoint);

                        }

                        else if (command == "DOWNLOAD_SHARD")
                        {
                            //download shard
                        }

                        else if (command == "UPDATE_TRANSACTION")
                        {
                            //update transaction for blockchain
                            StorageCommitmentTransaction newTransaction = JsonConvert.DeserializeObject<StorageCommitmentTransaction>(Encoding.UTF8.GetString(sharedData));
                            pendingTransactions.Add(newTransaction);

                            if (pendingTransactions.Count >= 3)
                            {
                                CreateBlock();
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Unknown request: {requestMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while processing request: {ex.Message}");
                    }
                }
            }
        }

        static void CreateBlock()
        {
            Block lastblock = blockchainCopy.Last();
            Block block = new Block
            {
                Index = lastblock.Index++,
                Timestamp = DateTime.UtcNow,
                PreviousHash = lastblock.BlockHash,
                Transactions = new List<StorageCommitmentTransaction>(pendingTransactions)
            };

            blockchainCopy.Add(block);
        }

            public static void SetGenesisAndLatestBlocks()
        {
            if (blockchainCopy.Count > 0)
            {
                // Genesis block is always the block with index 0
                GenesisBlock = blockchainCopy.First(b => b.Index == 0);

                // Latest block is the block with the highest index
                LatestBlock = blockchainCopy.OrderByDescending(b => b.Index).FirstOrDefault();
            }
            else
            {
                GenesisBlock = null;
                LatestBlock = null;
            }
        }


        public static void UpdateBlockchainFromJson(string jsonData)
        {
            var blockchainResponse = JsonConvert.DeserializeObject<dynamic>(jsonData);
            List<Block> receivedBlocks = JsonConvert.DeserializeObject<List<Block>>(blockchainResponse.blocks.ToString());

            // Update the global blockchain list with the received blocks
            blockchainCopy = receivedBlocks.ToList();

            SetGenesisAndLatestBlocks();

        }
    }
}
