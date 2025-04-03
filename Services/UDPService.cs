using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Test1.Models;

namespace Test1.Services
{
    public class UDPService : IDisposable
    {
        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _serverEndPoint;
        private readonly string _clientId;
        private readonly int _intervalSeconds;
        private CancellationTokenSource _cts;
        private bool _isDisposed;

        public event Action<string> LogMessage;
        public event Action<IPEndPoint, byte[]> DataReceived;

        public UDPService(string serverIp, int serverPort, string clientId, int intervalSeconds = 20)
        {
            _udpClient = new UdpClient(0); // Explicit random port
            _serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);
            _clientId = clientId;
            _intervalSeconds = intervalSeconds;
        }

        public async Task StartHolePunchingAsync()
        {
            if (_cts != null)
                throw new InvalidOperationException("Service is already running");

            _cts = new CancellationTokenSource();
            LogMessage?.Invoke($"Starting UDP keep-alive to {_serverEndPoint} every {_intervalSeconds}s");

            // Start receive loop
            _ = Task.Run(ReceiveLoopAsync, _cts.Token);

            // Start ping loop
            await Task.Run(async () =>
            {
                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        await SendPingAsync();
                        await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), _cts.Token);
                    }
                }
                catch (TaskCanceledException)
                {
                    LogMessage?.Invoke("Keep-alive stopped gracefully");
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Keep-alive error: {ex.Message}");
                }
            });
        }

        private async Task SendPingAsync()
        {
            try
            {
                var message = Encoding.UTF8.GetBytes($"KEEPALIVE|{_clientId}|{DateTime.UtcNow:o}");
                var sentBytes = await _udpClient.SendAsync(message, message.Length, _serverEndPoint);
                LogMessage?.Invoke($"Sent {sentBytes} bytes to {_serverEndPoint}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ping failed: {ex.Message}");
                throw;
            }
        }

        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var result = await _udpClient.ReceiveAsync(_cts.Token);
                    string recData = Encoding.UTF8.GetString(result.Buffer);
                    if (recData.StartsWith("PUNCHPEER|"))
                    {
                        string peerIP = recData.Split('|')[1];
                        string peerPort = recData.Split('|')[2];
                        IPEndPoint nodeEndpoint = new IPEndPoint(IPAddress.Parse(peerIP), int.Parse(peerPort));
                        byte[] msg = Encoding.UTF8.GetBytes("PUNCH");
                        for(int i = 0; i < 3; i++)
                        {
                            await SendAsync(msg, nodeEndpoint);
                        }
                    }

                    if (recData.StartsWith("DOWNLOADBC"))
                    {
                        string uploadBC = JsonConvert.SerializeObject(Blockchain.GetBlockchain());
                        string msg = "TAKEBC|" + uploadBC;
                        await SendAsync(Encoding.UTF8.GetBytes(uploadBC), result.RemoteEndPoint); 
                    }

                    if (recData.StartsWith("TAKEBC|"))
                    {
                        string newBlockchain = recData.Split('|')[1];
                        List<Block> deserializedBC = JsonConvert.DeserializeObject<List<Block>>(newBlockchain);
                        Blockchain.blockchain = deserializedBC;
                    }

                    LogMessage?.Invoke($"Received {result.Buffer.Length} bytes from {result.RemoteEndPoint}");
                    DataReceived?.Invoke(result.RemoteEndPoint, result.Buffer);
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke("Receive loop stopped");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Receive error: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            await Task.Delay(100); // Brief delay for clean shutdown
        }

        public async Task SendAsync(byte[] data, IPEndPoint endpoint)
        {
            await _udpClient.SendAsync(data, data.Length, endpoint);
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _cts?.Cancel();
            _udpClient?.Dispose();
            _cts?.Dispose();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        ~UDPService() => Dispose();
    }
}