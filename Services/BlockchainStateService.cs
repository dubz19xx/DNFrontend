using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Test1.Models;
using Test1.Utilities;

namespace Test1.Services
{
    internal class BlockchainStateService
    {
        public static async Task SaveBlockchainAsync(List<Block> blockchain)
        {
            try
            {
                string json = JsonConvert.SerializeObject(blockchain, Formatting.Indented);
                await File.WriteAllTextAsync(FileHelper.blockchainPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving blockchain: {ex.Message}");
                throw;
            }
        }

        public static async Task<List<Block>> LoadBlockchainAsync()
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
    }
}
