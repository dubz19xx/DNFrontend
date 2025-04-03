using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Test1.Models;
using Newtonsoft.Json;
using System.Threading;
using Test1.Utilities;

namespace Test1.Services
{
    public class P2PService
    {
        private readonly UDPService _udpService;
        private TcpListener _tcpListener;
        private CancellationTokenSource _tcpCts;

        public P2PService(UDPService udpService)
        {
            _udpService = udpService;
        }

        public async Task StartTcpListener(int port)
        {
            _tcpCts = new CancellationTokenSource();
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();

            _ = Task.Run(async () =>
            {
                while (!_tcpCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync(_tcpCts.Token);
                        _ = HandleTcpClientAsync(client);
                    }
                    catch (OperationCanceledException) { }
                }
            }, _tcpCts.Token);
        }

        private async Task HandleTcpClientAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                byte[] buffer = new byte[256 * 1024]; // 256KB buffer
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                byte[] receivedData = new byte[bytesRead];
                Array.Copy(buffer, receivedData, bytesRead);

                // Process received shard
                FileHelper.SaveShardLocally(Encoding.UTF8.GetString(receivedData));
            }
        }

        public async Task SendShardViaTcp(string ip, int port, byte[] data)
        {
            using (var tcpClient = new TcpClient())
            {
                await tcpClient.ConnectAsync(ip, port);
                using (var stream = tcpClient.GetStream())
                {
                    await stream.WriteAsync(data, 0, data.Length);
                }
            }
        }


        public async Task SendUDPmsg(string ip, int port, string prefix, string msg = "None")
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
            await _udpService.SendMessageAsync(prefix, msg, endpoint);
        }

        public async Task PunchPeers(List<OnlineNode> nodes)
        {
            await SendUDPmsg("4.188.232.157", 12345, "PUNCHPEERS",
                            JsonConvert.SerializeObject(nodes));
            await Task.Delay(5000); // Use Task.Delay instead of Thread.Sleep in async
        }
    }
}
