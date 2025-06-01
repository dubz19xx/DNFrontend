using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Test1.Services;
using Test1.Utilities;
using System.Security.Cryptography;
using System.Transactions;

namespace Test1.Models
{
    internal class Blockchain
    {
        private static readonly string _blockchainPath = Path.Combine(FileHelper.userFolderPath, "blockchain.json");
        public static UDPService udpService;
        public static P2PService p2pService; 
        private static readonly List<StorageCommitmentTransaction> _pendingTransactions = new List<StorageCommitmentTransaction>();
        private static readonly object _lock = new object();


        public static async Task InitializeBlockchainAsync()
        {
            Directory.CreateDirectory(FileHelper.userFolderPath);

            // Initialize network services
            udpService = new UDPService("74.225.135.66", 12345, AuthService.nodeAddress);
            p2pService = new P2PService(udpService);
            udpService.StartHolePunchingAsync();

            // Create blockchain file if it doesn't exist
            if (!File.Exists(_blockchainPath))
            {
                await CreateNewBlockchain();
            }
            else
            {
                // Validate existing blockchain
                var blockchain = await LoadBlockchain();
                if (!ValidateBlockchain(blockchain))
                {
                    Console.WriteLine("Invalid blockchain detected, creating new one");
                    await CreateNewBlockchain();
                }
            }

            await SyncWithNetwork();
        }


        private static async Task CreateNewBlockchain()
        {
            var genesisBlock = new Block
            {
                Index = 0,
                Timestamp = DateTime.UtcNow,
                PreviousHash = "0",
                Transactions = new List<StorageCommitmentTransaction>(),
                BlockHash = CalculateHash(0, DateTime.UtcNow, "0", string.Empty)
            };

            await SaveBlockchain(new List<Block> { genesisBlock });
        }

        public static async Task<List<Block>> GetBlockchain()
        {
            return await LoadBlockchain();
        }

        public static async Task AddTransaction(StorageCommitmentTransaction transaction)
        {
            // First check if this transaction already exists in pending or blockchain
            bool isDuplicate = await IsDuplicateTransaction(transaction.ChunkHash);
            if (isDuplicate)
            {
                Console.WriteLine($"Duplicate transaction detected and skipped: {transaction.ChunkHash}");
                return;
            }

            lock (_lock)
            {
                // Check again inside lock to prevent race conditions
                if (_pendingTransactions.Any(t => t.ChunkHash == transaction.ChunkHash))
                    return;

                _pendingTransactions.Add(transaction);
            }

            // Only proceed if we have enough unique transactions
            if (_pendingTransactions.Count >= 3)
            {
                List<StorageCommitmentTransaction> transactionsToAdd;
                List<Block> blockchain;
                Block latestBlock;

                lock (_lock)
                {
                    transactionsToAdd = new List<StorageCommitmentTransaction>(_pendingTransactions);
                    _pendingTransactions.Clear();
                }

                blockchain = await GetBlockchain();
                latestBlock = blockchain.Last();

                var newBlock = new Block
                {
                    Index = latestBlock.Index + 1,
                    Timestamp = DateTime.UtcNow,
                    PreviousHash = latestBlock.BlockHash,
                    Transactions = transactionsToAdd,
                    BlockHash = string.Empty
                };

                newBlock.MerkleRoot = CalculateMerkleRoot(newBlock.Transactions);
                newBlock.BlockHash = CalculateBlockHash(newBlock);

                blockchain.Add(newBlock);
                await SaveBlockchain(blockchain);

                _ = Task.Run(() => BroadcastNewBlock(newBlock));
            }
        }

        private static async Task<bool> IsDuplicateTransaction(string chunkHash)
        {
            // Check pending transactions
            lock (_lock)
            {
                if (_pendingTransactions.Any(t => t.ChunkHash == chunkHash))
                    return true;
            }

            // Check existing blockchain
            var blockchain = await GetBlockchain();
            return blockchain
                .SelectMany(b => b.Transactions)
                .Any(t => t.ChunkHash == chunkHash);
        }

        private static async Task BroadcastNewBlock(Block newBlock)
        {
            var onlineNodes = await GetOnlineNodes();
            if (onlineNodes.Count > 0)
            {
                var blockJson = JsonConvert.SerializeObject(newBlock);
                foreach (var node in onlineNodes)
                {
                    await p2pService.SendUDPmsg(node.ipAddress, node.port, "NEWBLOCK", blockJson);
                }
            }
        }

        public static async Task UpdateBlockchain(List<Block> newBlockchain)
        {
            if (newBlockchain == null || newBlockchain.Count == 0)
                throw new ArgumentException("Blockchain cannot be empty");

            if (!ValidateBlockchain(newBlockchain))
                throw new InvalidOperationException("Invalid blockchain received");

            await SaveBlockchain(newBlockchain);
        }

        private static async Task<List<Block>> LoadBlockchain()
        {
            try
            {
                if (!File.Exists(_blockchainPath))
                    return new List<Block>();

                var json = await File.ReadAllTextAsync(_blockchainPath);
                return JsonConvert.DeserializeObject<List<Block>>(json) ?? new List<Block>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading blockchain: {ex.Message}");
                return new List<Block>();
            }
        }

        private static async Task SaveBlockchain(List<Block> blockchain)
        {
            try
            {
                var json = JsonConvert.SerializeObject(blockchain, Formatting.Indented);
                await File.WriteAllTextAsync(_blockchainPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving blockchain: {ex.Message}");
                throw;
            }
        }

        private static bool ValidateBlockchain(List<Block> blockchain)
        {
            if (blockchain == null || blockchain.Count == 0)
                return false;

            // Validate genesis block
            if (blockchain[0].Index != 0 ||
                blockchain[0].PreviousHash != "0" ||
                blockchain[0].Transactions.Count != 0)
                return false;

            // Validate subsequent blocks
            for (int i = 1; i < blockchain.Count; i++)
            {
                var block = blockchain[i];
                var prevBlock = blockchain[i - 1];

                if (block.Index != i ||
                    block.PreviousHash != prevBlock.BlockHash ||
                    block.BlockHash != CalculateBlockHash(block))
                    return false;
            }

            return true;
        }

        private static string CalculateBlockHash(Block block)
        {
            return CalculateHash(block.Index, block.Timestamp, block.PreviousHash, block.MerkleRoot);
        }

        private static string CalculateHash(int index, DateTime timestamp, string previousHash, string merkleRoot)
        {
            using (var sha256 = SHA256.Create())
            {
                var input = $"{index}{timestamp:O}{previousHash}{merkleRoot}";
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes).Replace("-", "").ToLower();
            }
        }

        public static async Task<OnlineNode> SelectBestNode()
        {
            List<OnlineNode> onlineNodes = await GetOnlineNodes();
            var random = new Random();
            int index = random.Next(onlineNodes.Count);
            return onlineNodes[index];
        }

        private static string CalculateMerkleRoot(List<StorageCommitmentTransaction> transactions)
        {
            if (transactions == null || transactions.Count == 0)
                return string.Empty;

            var hashes = transactions.Select(t => t.ChunkHash).ToArray();
            return CalculateMerkleRoot(hashes);
        }

        private static string CalculateMerkleRoot(string[] hashes)
        {
            // Simple Merkle root implementation
            if (hashes.Length == 1)
                return hashes[0];

            var newHashes = new List<string>();
            for (int i = 0; i < hashes.Length; i += 2)
            {
                var left = hashes[i];
                var right = (i + 1 < hashes.Length) ? hashes[i + 1] : left;
                newHashes.Add(CalculateHash(0, DateTime.UtcNow, left, right));
            }

            return CalculateMerkleRoot(newHashes.ToArray());
        }

        private static async Task SyncWithNetwork()
        {
            var onlineNodes = await GetOnlineNodes();
            if (onlineNodes.Count > 0)
            {
                await p2pService.PunchPeers(onlineNodes);
                foreach (var node in onlineNodes)
                {
                    await p2pService.SendUDPmsg(node.ipAddress, node.port, "DOWNLOADBC");
                }
            }
        }

        public static async Task<List<OnlineNode>> GetOnlineNodes()
        {
            HttpResponseMessage response = await NetworkService.SendGetRequest("OnlineNodes");
            string jsonResponse = await response.Content.ReadAsStringAsync();
            List<OnlineNode> nodes = JsonConvert.DeserializeObject<List<OnlineNode>>(jsonResponse);
            nodes.RemoveAll(node => node.dnAddress == AuthService.nodeAddress);
            return nodes;
        }
    }
}