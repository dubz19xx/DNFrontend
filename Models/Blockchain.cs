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

namespace Test1.Models
{
    internal class Blockchain
    {
        public static string NodeAddress;
        public static List<Block> blockchain = new List<Block>();
        public static Block GenesisBlock { get; private set; }
        public static Block LatestBlock { get; private set; }

        static List<StorageCommitmentTransaction> pendingTransactions = new List<StorageCommitmentTransaction>();

        public static NetworkService networkservice;

        public static UDPService udpService;
        public static P2PService p2pService;

        public Blockchain()
        {
        }

        public static Dictionary<string, List<string>> GetNodesForChunks(List<string> chunkHashes)
        {
            Dictionary<string, List<string>> chunkToNodesMap = new Dictionary<string, List<string>>();

            // Initialize the dictionary with all requested chunk hashes
            foreach (var hash in chunkHashes)
            {
                chunkToNodesMap[hash] = new List<string>();
            }


            // Traverse each block in the blockchain
            foreach (var block in blockchain)
            {
                // Check each transaction in the block
                foreach (var transaction in block.Transactions)
                {
                    // Only process STORAGE transactions
                    if (transaction.TransactionType == "STORAGE")
                    {
                        // Check if this transaction's chunk hash is one we're looking for
                        if (chunkToNodesMap.ContainsKey(transaction.ChunkHash))
                        {
                            // Add the node ID if not already present
                            if (!chunkToNodesMap[transaction.ChunkHash].Contains(transaction.NodeId))
                            {
                                chunkToNodesMap[transaction.ChunkHash].Add(transaction.NodeId);
                            }
                        }
                    }
                }
            }

            return chunkToNodesMap;
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
                    block.Index = LatestBlock.Index + 1;  // Changed from Index++ to Index + 1
                    block.PreviousHash = LatestBlock.BlockHash;
                    block.PreviousBlock = LatestBlock;
                }

                block.MerkleRoot = block.CalculateMerkleRoot();
                block.BlockHash = block.CalculateBlockHash();
                blockchain.Add(block);

                LatestBlock = block;

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

            //start udp listener and puncher
            udpService = new UDPService("4.188.232.157", 12345, AuthService.nodeAddress);
            p2pService = new P2PService(udpService);


            udpService.StartHolePunchingAsync();

            //get online nodes
            List<OnlineNode> onlineNodesList = await GetOnlineNodes();

            //if no one else online create a new blockchain
            if (onlineNodesList.Count > 0)
            {
                //download blockchain from other online node
                await p2pService.PunchPeers(onlineNodesList);
                foreach(OnlineNode node in onlineNodesList)
                {
                    await p2pService.SendUDPmsg(node.ipAddress, node.port, "DOWNLOADBC");
                }
            }
            else
            {
                //create new blockchain
                GenesisBlock = CreateGenesisBlock();
                LatestBlock = GenesisBlock;
                blockchain.Add(LatestBlock);
            }

            

        }

        public static void UpdateBlockchain(List<Block> newBC)
        {
            blockchain = new List<Block>(newBC);

            // Update the latest block reference
            LatestBlock = blockchain.Last();
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
        }



        // Retrieve the globally stored blockchain
        public static List<Block> GetBlockchain()
        {
            return blockchain;
        }


    }
}
