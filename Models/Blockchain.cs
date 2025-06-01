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

namespace Test1.Models
{
    internal class Blockchain
    {
        private static List<Block> _blockchain;
        public static Block GenesisBlock { get; private set; }
        public static Block LatestBlock { get; private set; }
        private static List<StorageCommitmentTransaction> _pendingTransactions = new List<StorageCommitmentTransaction>();

        public static string NodeAddress;
        public static UDPService udpService;
        public static P2PService p2pService;

        static Blockchain()
        {
            _blockchain = new List<Block>();
        }

        public static async Task InitializeBlockchainAsync()
        {
            // Initialize UDP services
            udpService = new UDPService("4.188.232.157", 12345, AuthService.nodeAddress);
            p2pService = new P2PService(udpService);
            await udpService.StartHolePunchingAsync();

            // Load or create blockchain
            _blockchain = await LoadBlockchainFromFile();

            if (_blockchain.Count == 0)
            {
                // Create new blockchain
                GenesisBlock = CreateGenesisBlock();
                LatestBlock = GenesisBlock;
                _blockchain.Add(GenesisBlock);
                await SaveBlockchainToFile();
            }
            else
            {
                // Initialize from loaded blockchain
                GenesisBlock = _blockchain.First();
                LatestBlock = _blockchain.Last();
            }

            // Sync with network if nodes available
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

        public static void AddTransaction(StorageCommitmentTransaction transaction)
        {
            _pendingTransactions.Add(transaction);

            if (_pendingTransactions.Count >= 3) // Create block after 3 transactions
            {
                CreateNewBlock(_pendingTransactions);
                _pendingTransactions.Clear();
            }
        }

        private static void CreateNewBlock(List<StorageCommitmentTransaction> transactions)
        {
            var newBlock = new Block
            {
                Index = LatestBlock.Index + 1,
                Timestamp = DateTime.Now,
                PreviousHash = LatestBlock.BlockHash,
                PreviousBlock = LatestBlock,
                Transactions = new List<StorageCommitmentTransaction>(transactions)
            };

            newBlock.MerkleRoot = newBlock.CalculateMerkleRoot();
            newBlock.BlockHash = newBlock.CalculateBlockHash();

            _blockchain.Add(newBlock);
            LatestBlock = newBlock;

            SaveBlockchainToFile().GetAwaiter().GetResult();
        }

        public static void UpdateBlockchain(List<Block> newBlockchain)
        {
            // Validate the new blockchain isn't empty
            if (newBlockchain == null || newBlockchain.Count == 0)
                throw new ArgumentException("Blockchain cannot be empty");

            // Completely replace existing blockchain
            _blockchain = new List<Block>(newBlockchain);
            GenesisBlock = _blockchain.First();
            LatestBlock = _blockchain.Last();

            SaveBlockchainToFile().GetAwaiter().GetResult();
        }

        private static async Task SaveBlockchainToFile()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_blockchain, Formatting.Indented);
                await File.WriteAllTextAsync(FileHelper.blockchainPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving blockchain: {ex.Message}");
                throw;
            }
        }

        private static async Task<List<Block>> LoadBlockchainFromFile()
        {
            try
            {
                if (!File.Exists(FileHelper.blockchainPath))
                {
                    return new List<Block>();
                }

                string json = await File.ReadAllTextAsync(FileHelper.blockchainPath);
                return JsonConvert.DeserializeObject<List<Block>>(json) ?? new List<Block>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading blockchain: {ex.Message}");
                return new List<Block>();
            }
        }

        public static List<Block> GetBlockchain()
        {
            return _blockchain ?? LoadBlockchainFromFile().GetAwaiter().GetResult();
        }

        public static async Task<List<OnlineNode>> GetOnlineNodes()
        {
            HttpResponseMessage response = await NetworkService.SendGetRequest("OnlineNodes");
            string jsonResponse = await response.Content.ReadAsStringAsync();
            List<OnlineNode> nodes = JsonConvert.DeserializeObject<List<OnlineNode>>(jsonResponse);
            nodes.RemoveAll(node => node.dnAddress == AuthService.nodeAddress);
            return nodes;
        }

        public static async Task<OnlineNode> SelectBestNode()
        {
            List<OnlineNode> onlineNodes = await GetOnlineNodes();
            return onlineNodes.Count == 0 ? null : onlineNodes[new Random().Next(onlineNodes.Count)];
        }

        private static Block CreateGenesisBlock()
        {
            return new Block
            {
                Index = 0,
                Timestamp = DateTime.Now,
                PreviousHash = string.Empty,
                Transactions = new List<StorageCommitmentTransaction>(),
                BlockHash = "0" // Special hash for genesis block
            };
        }

        public static bool ValidateBlockchain()
        {
            if (_blockchain == null || _blockchain.Count == 0)
                return false;

            // Verify genesis block
            if (_blockchain[0].Index != 0 || _blockchain[0].PreviousHash != string.Empty)
                return false;

            // Verify subsequent blocks
            for (int i = 1; i < _blockchain.Count; i++)
            {
                if (_blockchain[i].Index != i)
                    return false;

                if (_blockchain[i].PreviousHash != _blockchain[i - 1].BlockHash)
                    return false;

                if (_blockchain[i].BlockHash != _blockchain[i].CalculateBlockHash())
                    return false;
            }

            return true;
        }
    }
}