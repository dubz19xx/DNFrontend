using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System;
using Test1.Models;
using Test1.Services;

public class P2PService
{
    private readonly UDPService _udpService;
    private const int MaxChunkSize = 1200; // Safe UDP payload size
    private const int MaxChunksPerSecond = 1000; // Rate limiting

    public P2PService(UDPService udpService)
    {
        _udpService = udpService;
    }

    public async Task SendUDPmsg(string ip, int port, string prefix, string msg = "None")
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(ip), port);

        // Check if message needs chunking
        byte[] fullMessage = Encoding.UTF8.GetBytes($"{prefix}|{msg}");
        if (fullMessage.Length <= MaxChunkSize)
        {
            await _udpService.SendAsync(fullMessage, endpoint);
            return;
        }

        // Chunking logic for large messages
        await SendChunkedMessage(fullMessage, prefix, endpoint);
    }

    private async Task SendChunkedMessage(byte[] fullData, string originalPrefix, IPEndPoint endpoint)
    {
        // Generate unique message ID
        string messageId = Guid.NewGuid().ToString("N");
        int totalChunks = (int)Math.Ceiling(fullData.Length / (double)MaxChunkSize);

        // Rate limiter to avoid flooding
        var rateLimiter = new System.Diagnostics.Stopwatch();
        rateLimiter.Start();
        int chunksSent = 0;

        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            int offset = chunkIndex * MaxChunkSize;
            int chunkSize = Math.Min(MaxChunkSize, fullData.Length - offset);
            byte[] chunkData = new byte[chunkSize];
            Array.Copy(fullData, offset, chunkData, 0, chunkSize);

            // Build chunk header
            string chunkHeader = $"CHUNKED|{messageId}|{chunkIndex}|{totalChunks}|{originalPrefix}|";
            byte[] headerBytes = Encoding.UTF8.GetBytes(chunkHeader);

            // Combine header and data
            byte[] packet = new byte[headerBytes.Length + chunkSize];
            Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);
            Buffer.BlockCopy(chunkData, 0, packet, headerBytes.Length, chunkSize);

            // Send with rate limiting
            await _udpService.SendAsync(packet, endpoint);
            chunksSent++;

            // Respect rate limit
            if (rateLimiter.ElapsedMilliseconds < 1000 && chunksSent >= MaxChunksPerSecond)
            {
                await Task.Delay(1000 - (int)rateLimiter.ElapsedMilliseconds);
                rateLimiter.Restart();
                chunksSent = 0;
            }
        }
    }

    public async Task PunchPeers(List<OnlineNode> nodes)
    {
        await SendUDPmsg("4.188.232.157", 12345, "PUNCHPEERS",
                        JsonConvert.SerializeObject(nodes));
        await Task.Delay(5000);
    }

}

