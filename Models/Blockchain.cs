using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Test1.Services;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using System.Net;
using System.Runtime.CompilerServices;
using System.IO;
using Test1.Utilities;

namespace Test1.Models
{
    internal class Blockchain
    {
        public static string NodeAddress;
        private static List<Block> _blockchain;
        public static Block GenesisBlock { get; private set; }
        public static Block LatestBlock { get; private set; }

        static List<StorageCommitmentTransaction> pendingTransactions = new List<StorageCommitmentTransaction>();

        public static NetworkService networkservice;

        public static UDPService udpService;
        public static P2PService p2pService;

        public Blockchain()
        {
        }

        public static List<Block> blockchain
        {
            get
            {
                if (_blockchain == null)
                {
                    _blockchain = LoadBlockchainFromFile().GetAwaiter().GetResult();
                }
                return _blockchain;
            }
            private set
            {
                _blockchain = value;
                SaveBlockchainToFile().GetAwaiter().GetResult();
            }
        }

        public static void AddTransaction(StorageCommitmentTransaction transaction)
        {
            pendingTransactions.Add(transaction);
            if (pendingTransactions.Count > 2)
            {
                Block block = new Block();
                foreach (StorageCommitmentTransaction txn in pendingTransactions)
                {
                    block.Transactions.Add(txn);
                }
                block.Timestamp = DateTime.Now;
                if (LatestBlock == null)
                {
                    // This should be the first block after genesis
                    block.Index = 1;
                    block.PreviousHash = GenesisBlock.BlockHash;
                    block.PreviousBlock = GenesisBlock;
                }
                else
                {
                    block.Index = LatestBlock.Index + 1;
                    block.PreviousHash = LatestBlock.BlockHash;
                    block.PreviousBlock = LatestBlock;
                }

                block.MerkleRoot = block.CalculateMerkleRoot();
                block.BlockHash = block.CalculateBlockHash();
                blockchain.Add(block);

                LatestBlock = block;
                pendingTransactions.Clear();

                // Save to file after adding block
                SaveBlockchainToFile().GetAwaiter().GetResult();
            }
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
                    return new List<Block>(); // Return empty blockchain if file doesn't exist
                }

                string json = await File.ReadAllTextAsync(FileHelper.blockchainPath);
                return JsonConvert.DeserializeObject<List<Block>>(json) ?? new List<Block>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading blockchain: {ex.Message}");
                return new List<Block>(); // Return empty blockchain if error occurs
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

        public static async Task<OnlineNode> SelectBestNode()
        {
            List<OnlineNode> onlineNodes = await GetOnlineNodes();
            var random = new Random();
            int index = random.Next(onlineNodes.Count);
            return onlineNodes[index];
        }

        public static async Task InitializeBlockchainAsync()
        {
            // Start udp listener and puncher
            udpService = new UDPService("74.225.135.66", 12345, AuthService.nodeAddress);
            p2pService = new P2PService(udpService);

            udpService.StartHolePunchingAsync();

            // Get online nodes
            List<OnlineNode> onlineNodesList = await GetOnlineNodes();

            // If no one else online create a new blockchain
            if (onlineNodesList.Count > 0)
            {
                // Download blockchain from other online node
                await p2pService.PunchPeers(onlineNodesList);
                foreach (OnlineNode node in onlineNodesList)
                {
                    await p2pService.SendUDPmsg(node.ipAddress, node.port, "DOWNLOADBC");
                }
            }
            else
            {
                // Create new blockchain
                GenesisBlock = CreateGenesisBlock();
                LatestBlock = GenesisBlock;
                blockchain = new List<Block> { GenesisBlock };
                await SaveBlockchainToFile();
            }
        }

        public static void UpdateBlockchain(List<Block> newBC)
        {
            blockchain = new List<Block>(newBC);
            LatestBlock = blockchain.Last();
            SaveBlockchainToFile().GetAwaiter().GetResult();
        }

        public static Block CreateGenesisBlock()
        {
            return new Block
            {
                Index = 0,
                Timestamp = DateTime.Now,
                PreviousHash = string.Empty,
                Transactions = new List<StorageCommitmentTransaction>(),
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

            blockchain.Add(newBlock);
            SaveBlockchainToFile().GetAwaiter().GetResult();
        }

        // Retrieve the globally stored blockchain
        public static List<Block> GetBlockchain()
        {
            return blockchain;
        }
    }
}