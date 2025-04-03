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
    private const int MaxChunkSize = 1200;

    public P2PService(UDPService udpService)
    {
        _udpService = udpService;
    }

    // New method for sending raw bytes
    public async Task SendUDPmsg(string ip, int port, byte[] data)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(ip), port);

        if (data.Length <= MaxChunkSize)
        {
            await _udpService.SendAsync(data, endpoint);
            return;
        }

        // Chunking logic
        string messageId = Guid.NewGuid().ToString("N");
        int totalChunks = (int)Math.Ceiling(data.Length / (double)MaxChunkSize);

        for (int i = 0; i < totalChunks; i++)
        {
            int offset = i * MaxChunkSize;
            int chunkSize = Math.Min(MaxChunkSize, data.Length - offset);

            // Build chunk header
            string header = $"CHUNKED|{messageId}|{i}|{totalChunks}|SAVESHARD|";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);

            // Create packet
            byte[] packet = new byte[headerBytes.Length + chunkSize];
            Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);
            Buffer.BlockCopy(data, offset, packet, headerBytes.Length, chunkSize);

            await _udpService.SendAsync(packet, endpoint);

            // Small delay to prevent flooding
            if (i % 10 == 0) await Task.Delay(1);
        }
    }

    // Keep existing methods for string messages
    public async Task SendUDPmsg(string ip, int port, string prefix, string msg = "None")
    {
        var message = Encoding.UTF8.GetBytes($"{prefix}|{msg}");
        await SendUDPmsg(ip, port, message);
    }

    public async Task PunchPeers(List<OnlineNode> nodes)
    {
        await SendUDPmsg("4.188.232.157", 12345, "PUNCHPEERS",
                        JsonConvert.SerializeObject(nodes));
        await Task.Delay(5000);
    }
}