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

namespace Test1.Models
{
    internal class Blockchain
    {
        public static List<Block> blockchain = new List<Block>();
        public static Block GenesisBlock { get; private set; }
        public static Block LatestBlock { get; private set; }

        static List<StorageCommitmentTransaction> pendingTransactions = new List<StorageCommitmentTransaction>();

        public static NetworkService networkservice;
        public Blockchain()
        {
        }

        public static async Task<List<OnlineNode>> GetOnlineNodes()
        {
            HttpResponseMessage response = await NetworkService.SendGetRequest("OnlineNodes");
            string jsonResponse = await response.Content.ReadAsStringAsync();

            List<OnlineNode> nodes = JsonConvert.DeserializeObject<List<OnlineNode>>(jsonResponse);

            nodes.RemoveAll(node => node.dnAddress == AuthService.nodeAddress);

            return nodes;

        }

        public static async Task InitializeBlockchainAsync()
        {

            //start udp listener and puncher
            var udpService = new UDPService("4.188.232.157", 12345, AuthService.nodeAddress);
            var p2pService = new P2PService(udpService);

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

            }

            Block GenesisBlock = CreateGenesisBlock();
            Block LatestBlock = GenesisBlock;
            blockchain.Add(LatestBlock);

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
