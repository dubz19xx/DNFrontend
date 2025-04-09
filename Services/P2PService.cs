using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System;
using Test1.Models;
using Test1.Services;
using System.Linq;

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

        string messageId = Guid.NewGuid().ToString("N");
        int totalChunks = (int)Math.Ceiling(data.Length / (double)MaxChunkSize);
        var acksReceived = new bool[totalChunks];
        var pendingChunks = new List<int>(Enumerable.Range(0, totalChunks));

        _udpService.RegisterAckListener(messageId, (index) =>
        {
            acksReceived[index] = true;
        });

        for (int attempt = 0; attempt < 5 && pendingChunks.Count > 0; attempt++)
        {
            foreach (int i in pendingChunks.ToList())
            {
                int offset = i * MaxChunkSize;
                int chunkSize = Math.Min(MaxChunkSize, data.Length - offset);

                string header = $"CHUNKED|{messageId}|{i}|{totalChunks}|SAVESHARD|";
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                byte[] packet = new byte[headerBytes.Length + chunkSize];
                Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);
                Buffer.BlockCopy(data, offset, packet, headerBytes.Length, chunkSize);

                await _udpService.SendAsync(packet, endpoint);
            }

            await Task.Delay(300);
            pendingChunks = pendingChunks.Where(i => !acksReceived[i]).ToList();
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