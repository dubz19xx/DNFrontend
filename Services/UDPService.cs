using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Test1.Models;
using Test1.Utilities;

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
        public IPEndPoint LocalEndPoint => (IPEndPoint)_udpClient.Client.LocalEndPoint;
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
            // Dictionary to hold incomplete messages (key = messageId, value = chunks)
            Dictionary<string, List<byte[]>> incompleteMessages = new Dictionary<string, List<byte[]>>();

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var result = await _udpClient.ReceiveAsync(_cts.Token);
                    string recData = Encoding.UTF8.GetString(result.Buffer);

                    // Handle chunked messages
                    if (recData.Contains("|CHUNKED|"))
                    {
                        string[] parts = recData.Split('|');
                        string prefix = parts[0];
                        int chunkIndex = int.Parse(parts[2]);
                        int totalChunks = int.Parse(parts[3]);

                        // Extract the actual chunk data (everything after the header)
                        int headerLength = parts[0].Length + parts[1].Length + parts[2].Length + parts[3].Length + 4; // +4 for the | separators
                        byte[] chunkData = new byte[result.Buffer.Length - headerLength];
                        Array.Copy(result.Buffer, headerLength, chunkData, 0, chunkData.Length);

                        // Create a unique message ID based on sender and prefix
                        string messageId = $"{result.RemoteEndPoint}-{prefix}";

                        if (!incompleteMessages.ContainsKey(messageId))
                        {
                            incompleteMessages[messageId] = new List<byte[]>(new byte[totalChunks][]);
                        }

                        // Store the chunk
                        incompleteMessages[messageId][chunkIndex] = chunkData;

                        // Check if all chunks have arrived
                        if (incompleteMessages[messageId].All(c => c != null))
                        {
                            // Reassemble the complete message
                            int totalLength = incompleteMessages[messageId].Sum(c => c.Length);
                            byte[] completeMessage = new byte[totalLength];
                            int offset = 0;
                            foreach (var chunk in incompleteMessages[messageId])
                            {
                                Buffer.BlockCopy(chunk, 0, completeMessage, offset, chunk.Length);
                                offset += chunk.Length;
                            }

                            // Remove from incomplete messages
                            incompleteMessages.Remove(messageId);

                            // Process the complete message
                            string completeData = Encoding.UTF8.GetString(completeMessage);
                            await ProcessCompleteMessage(completeData, result.RemoteEndPoint);
                        }
                        continue;
                    }

                    // Handle non-chunked messages (your existing code)
                    if (recData.StartsWith("PUNCHPEER|"))
                    {
                        string peerIP = recData.Split('|')[1];
                        string peerPort = recData.Split('|')[2];
                        IPEndPoint nodeEndpoint = new IPEndPoint(IPAddress.Parse(peerIP), int.Parse(peerPort));
                        byte[] msg = Encoding.UTF8.GetBytes("PUNCH");
                        for (int i = 0; i < 3; i++)
                        {
                            await SendAsync(msg, nodeEndpoint);
                        }
                    }
                    else if (recData.StartsWith("PUNCH"))
                    {
                        byte[] msg = Encoding.UTF8.GetBytes("PUNCH");
                        await SendAsync(msg, result.RemoteEndPoint);
                    }
                    else if (recData.StartsWith("DOWNLOADBC"))
                    {
                        string uploadBC = JsonConvert.SerializeObject(Blockchain.GetBlockchain());
                        string msg = "TAKEBC|" + uploadBC;
                        await SendAsync(Encoding.UTF8.GetBytes(msg), result.RemoteEndPoint);
                    }
                    else if (recData.StartsWith("TAKEBC|"))
                    {
                        string newBlockchain = recData.Split('|')[1];
                        List<Block> deserializedBC = JsonConvert.DeserializeObject<List<Block>>(newBlockchain);
                        Blockchain.blockchain = deserializedBC;
                    }
                    else if (recData.StartsWith("SAVESHARD|"))
                    {
                        string saveData = recData.Split('|')[1];
                        FileHelper.SaveShardLocally(saveData);
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

        private async Task ProcessCompleteMessage(string completeData, IPEndPoint remoteEndPoint)
        {
            // Process the complete reassembled message
            if (completeData.StartsWith("SAVESHARD|"))
            {
                string saveData = completeData.Split('|')[1];
                FileHelper.SaveShardLocally(saveData);
            }
            else if (completeData.StartsWith("TAKEBC|"))
            {
                string newBlockchain = completeData.Split('|')[1];
                List<Block> deserializedBC = JsonConvert.DeserializeObject<List<Block>>(newBlockchain);
                Blockchain.blockchain = deserializedBC;
            }
            // Add other message types as needed

            LogMessage?.Invoke($"Processed complete message from {remoteEndPoint}");
            DataReceived?.Invoke(remoteEndPoint, Encoding.UTF8.GetBytes(completeData));
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

        public async Task SendMessageAsync(string prefix, string message, IPEndPoint endpoint = null)
        {
            const int maxChunkSize = 60000; // Safe size below typical UDP limits
            byte[] fullMessage = Encoding.UTF8.GetBytes($"{prefix}|{message}");

            if (fullMessage.Length <= maxChunkSize)
            {
                // Send as single packet if small enough
                await _udpClient.SendAsync(fullMessage, fullMessage.Length, endpoint ?? _serverEndPoint);
            }
            else
            {
                // Split into chunks
                int chunkCount = (int)Math.Ceiling((double)fullMessage.Length / maxChunkSize);
                for (int i = 0; i < chunkCount; i++)
                {
                    int offset = i * maxChunkSize;
                    int length = Math.Min(maxChunkSize, fullMessage.Length - offset);
                    byte[] chunk = new byte[length];
                    Array.Copy(fullMessage, offset, chunk, 0, length);

                    // Add chunk header (prefix|chunkIndex|chunkCount|)
                    string chunkHeader = $"{prefix}|CHUNKED|{i}|{chunkCount}|";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(chunkHeader);
                    byte[] packet = new byte[headerBytes.Length + length];

                    Array.Copy(headerBytes, 0, packet, 0, headerBytes.Length);
                    Array.Copy(chunk, 0, packet, headerBytes.Length, length);

                    await _udpClient.SendAsync(packet, packet.Length, endpoint ?? _serverEndPoint);
                }
            }
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