using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Test1.Services;
using Test1.Utilities;

namespace Test1.Models
{
    internal class Blockchain
    {
        private static readonly string _blockchainPath = Path.Combine(FileHelper.userFolderPath, "blockchain.json");
        public static UDPService udpService;
        public static P2PService p2pService;

        public static async Task InitializeBlockchainAsync()
        {
            // Ensure directory exists
            Directory.CreateDirectory(FileHelper.userFolderPath);

            // Initialize network services
            udpService = new UDPService("74.225.135.66", 12345, AuthService.nodeAddress);
            p2pService = new P2PService(udpService);
            await udpService.StartHolePunchingAsync();

            // Create blockchain file if it doesn't exist
            if (!File.Exists(_blockchainPath))
            {
                await CreateNewBlockchain();
            }

            // Sync with network
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

            var blockchain = new List<Block> { genesisBlock };
            await SaveBlockchain(blockchain);
        }

        public static async Task<List<Block>> GetBlockchain()
        {
            return await LoadBlockchain() ?? new List<Block>();
        }

        public static async Task AddTransaction(StorageCommitmentTransaction transaction)
        {
            var blockchain = await LoadBlockchain();
            var pendingTransactions = new List<StorageCommitmentTransaction> { transaction };

            // Get existing pending transactions if any
            if (blockchain.Last().Transactions.Count < 3)
            {
                pendingTransactions.AddRange(blockchain.Last().Transactions);
            }

            if (pendingTransactions.Count >= 3)
            {
                var latestBlock = blockchain.Last();
                var newBlock = new Block
                {
                    Index = latestBlock.Index + 1,
                    Timestamp = DateTime.UtcNow,
                    PreviousHash = latestBlock.BlockHash,
                    Transactions = pendingTransactions,
                    BlockHash = string.Empty // Temporary empty hash
                };

                newBlock.MerkleRoot = CalculateMerkleRoot(newBlock.Transactions);
                newBlock.BlockHash = CalculateBlockHash(newBlock);
                blockchain.Add(newBlock);

                await SaveBlockchain(blockchain);
            }
        }

        public static async Task UpdateBlockchain(List<Block> newBlockchain)
        {
            if (newBlockchain == null || newBlockchain.Count == 0)
                throw new ArgumentException("Blockchain cannot be empty");

            // Validate the new blockchain
            if (!ValidateBlockchain(newBlockchain))
                throw new InvalidOperationException("Invalid blockchain received");

            await SaveBlockchain(newBlockchain);
        }

        private static async Task<List<Block>> LoadBlockchain()
        {
            try
            {
                if (!File.Exists(_blockchainPath))
                    return null;

                var json = await File.ReadAllTextAsync(_blockchainPath);
                return JsonConvert.DeserializeObject<List<Block>>(json);
            }
            catch
            {
                return null;
            }
        }

        private static async Task SaveBlockchain(List<Block> blockchain)
        {
            var json = JsonConvert.SerializeObject(blockchain, Formatting.Indented);
            await File.WriteAllTextAsync(_blockchainPath, json);
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