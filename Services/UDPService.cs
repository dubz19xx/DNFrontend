using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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

        // Chunked message tracking
        private readonly Dictionary<string, ChunkedMessage> _incompleteMessages = new Dictionary<string, ChunkedMessage>();
        private readonly Timer _cleanupTimer;
        private const int ChunkTimeoutSeconds = 30;

        public event Action<string> LogMessage;
        public event Action<IPEndPoint, byte[]> DataReceived;

        public UDPService(string serverIp, int serverPort, string clientId, int intervalSeconds = 20)
        {
            _udpClient = new UdpClient(0); // Explicit random port
            _serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);
            _clientId = clientId;
            _intervalSeconds = intervalSeconds;

        }

        public IPEndPoint LocalEndPoint => (IPEndPoint)_udpClient.Client.LocalEndPoint;

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
                while (!_cts.Token.IsCancellationRequested)
                {
                    await SendPingAsync();
                    await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), _cts.Token);
                }
            });
        }

        private async Task SendPingAsync()
        {
            try
            {
                var message = Encoding.UTF8.GetBytes($"KEEPALIVE|{_clientId}|{DateTime.UtcNow:o}");
                await _udpClient.SendAsync(message, message.Length, _serverEndPoint);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ping failed: {ex.Message}");
            }
        }

        private readonly ConcurrentDictionary<string, Action<int>> _ackListeners = new();

        public void RegisterAckListener(string messageId, Action<int> ackHandler)
        {
            _ackListeners[messageId] = ackHandler;
        }

        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var result = await _udpClient.ReceiveAsync(_cts.Token);

                    string dataStr = Encoding.UTF8.GetString(result.Buffer);

                    // Handle ACK
                    if (dataStr.StartsWith("ACK|"))
                    {
                        string[] parts = dataStr.Split('|');
                        if (parts.Length >= 3 && _ackListeners.TryGetValue(parts[1], out var handler))
                        {
                            if (int.TryParse(parts[2], out int index))
                                handler?.Invoke(index);
                        }
                        continue;
                    }

                    // Existing chunk handler
                    if (TryProcessChunk(result.Buffer, result.RemoteEndPoint))
                        continue;

                    // All your original handlers like PUNCHPEER, DOWNLOADBC, etc.
                    string recData = dataStr;
                    if (recData.StartsWith("PUNCHPEER|"))
                    {
                        string peerIP = recData.Split('|')[1];
                        string peerPort = recData.Split('|')[2];
                        IPEndPoint nodeEndpoint = new IPEndPoint(IPAddress.Parse(peerIP), int.Parse(peerPort));
                        byte[] msg = Encoding.UTF8.GetBytes("PUNCH");
                        for (int i = 0; i < 3; i++) await SendAsync(msg, nodeEndpoint);
                    }
                    else if (recData.StartsWith("PUNCH"))
                    {
                        await SendAsync(Encoding.UTF8.GetBytes("PUNCH"), result.RemoteEndPoint);
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
                        Blockchain.UpdateBlockchain(deserializedBC);
                    }
                    else if (recData.StartsWith("ADDTRANSACTION|"))
                    {
                        string strTransaction = recData.Split("|")[2];
                        StorageCommitmentTransaction transaction = JsonConvert.DeserializeObject<StorageCommitmentTransaction>(strTransaction);
                        Blockchain.AddTransaction(transaction);
                    }
                    else if (recData.StartsWith("DOWNLOADSHARD|"))
                    {
                        string shardHash = recData.Split("|")[2];
                        byte[] shardData = FileHelper.RetrieveShards(shardHash);
                        byte[] shardRetrievalMsg = Encoding.UTF8.GetBytes("TAKESHARD|").Concat(shardData).ToArray();
                        await SendAsync(shardRetrievalMsg, result.RemoteEndPoint);
                    }
                    else if (recData.StartsWith("TAKESHARD|"))
                    {
                        string shardData = recData.Split("|")[1];
                        FileHelper.StoreShard(Encoding.UTF8.GetBytes(shardData));
                    }
                    else if (recData.StartsWith("SAVESHARD|"))
                    {
                        int headerLength = Encoding.UTF8.GetBytes("SAVESHARD|").Length;
                        if (result.Buffer.Length <= headerLength) continue;

                        byte[] shardData = new byte[result.Buffer.Length - headerLength];
                        Buffer.BlockCopy(result.Buffer, headerLength, shardData, 0, shardData.Length);
                        FileHelper.SaveShardLocally(shardData);
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

        private bool TryProcessChunk(byte[] data, IPEndPoint sender)
        {
            try
            {
                string header = Encoding.UTF8.GetString(data.Take(150).ToArray());
                string[] headerParts = header.Split('|');

                if (headerParts.Length < 6 || headerParts[0] != "CHUNKED")
                    return false;

                string messageId = headerParts[1];
                int chunkIndex = int.Parse(headerParts[2]);
                int totalChunks = int.Parse(headerParts[3]);
                string originalPrefix = headerParts[4];

                // Calculate header length
                int headerLength = Encoding.UTF8.GetBytes(string.Join("|", headerParts.Take(5)) + "|").Length;

                // Get or create message buffer
                if (!_incompleteMessages.TryGetValue(messageId, out var message))
                {
                    message = new ChunkedMessage(totalChunks, originalPrefix, DateTime.UtcNow);
                    _incompleteMessages[messageId] = message;
                }

                // Store chunk data
                byte[] chunkData = new byte[data.Length - headerLength];
                Buffer.BlockCopy(data, headerLength, chunkData, 0, chunkData.Length);
                message.AddChunk(chunkIndex, chunkData);

                // Check if complete
                if (message.IsComplete)
                {
                    byte[] fullData = message.GetFullData();
                    _incompleteMessages.Remove(messageId);
                    ProcessCompleteMessage(fullData, sender, originalPrefix);
                }


                // In TryProcessChunk after receiving a chunk:
                byte[] ack = Encoding.UTF8.GetBytes($"ACK|{messageId}|{chunkIndex}");
                _ = SendAsync(ack, sender);

                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Chunk processing error: {ex.Message}");
                return false;
            }
        }

        private void ProcessCompleteMessage(byte[] fullData, IPEndPoint sender, string prefix)
        {
            try
            {
                // For SAVESHARD messages, we expect binary data after the prefix
                if (prefix == "SAVESHARD")
                {
                    // Find where the actual binary data starts (after "SAVESHARD|")
                    int dataStart = Encoding.UTF8.GetBytes("SAVESHARD|").Length;
                    if (dataStart >= fullData.Length)
                    {
                        LogMessage?.Invoke("Invalid SAVESHARD format");
                        return;
                    }

                    byte[] shardData = new byte[fullData.Length - dataStart];
                    Buffer.BlockCopy(fullData, dataStart, shardData, 0, shardData.Length);

                    FileHelper.SaveShardLocally(shardData);
                }
                else if (prefix == "TAKEBC")
                {
                    string fullMessage = Encoding.UTF8.GetString(fullData);
                    string newBlockchain = fullMessage.Split('|')[1];
                    List<Block> deserializedBC = JsonConvert.DeserializeObject<List<Block>>(newBlockchain);
                    Blockchain.UpdateBlockchain(deserializedBC);
                }
                // Add other message types as needed
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error processing {prefix} message: {ex.Message}");
            }
        }

        private void CleanupIncompleteMessages(object state)
        {
            var now = DateTime.UtcNow;
            var toRemove = new List<string>();

            foreach (var kvp in _incompleteMessages)
            {
                if ((now - kvp.Value.LastUpdated).TotalSeconds > ChunkTimeoutSeconds)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _incompleteMessages.Remove(key);
                LogMessage?.Invoke($"Cleaned up incomplete message: {key}");
            }
        }

        public async Task SendAsync(byte[] data, IPEndPoint endpoint)
        {
            await _udpClient.SendAsync(data, data.Length, endpoint);
        }

        public async Task SendMessageAsync(string prefix, string message, IPEndPoint endpoint = null)
        {
            var msg = Encoding.UTF8.GetBytes($"{prefix}|{message}");
            await _udpClient.SendAsync(msg, msg.Length, endpoint ?? _serverEndPoint);
        }

        public async Task StopAsync()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            await Task.Delay(100); // Brief delay for clean shutdown
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _cleanupTimer?.Dispose();
            _cts?.Cancel();
            _udpClient?.Dispose();
            _cts?.Dispose();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        ~UDPService() => Dispose();

        private class ChunkedMessage
        {
            public byte[][] Chunks { get; }
            public string Prefix { get; }
            public DateTime LastUpdated { get; private set; }
            public int ReceivedCount { get; private set; }
            public bool IsComplete => ReceivedCount == Chunks.Length;

            public ChunkedMessage(int totalChunks, string prefix, DateTime created)
            {
                Chunks = new byte[totalChunks][];
                Prefix = prefix;
                LastUpdated = created;
            }

            public void AddChunk(int index, byte[] data)
            {
                if (Chunks[index] == null) ReceivedCount++;
                Chunks[index] = data;
                LastUpdated = DateTime.UtcNow;
            }

            public byte[] GetFullData()
            {
                int totalSize = Chunks.Sum(c => c?.Length ?? 0);
                byte[] result = new byte[totalSize];
                int offset = 0;
                foreach (var chunk in Chunks)
                {
                    if (chunk != null)
                    {
                        Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                        offset += chunk.Length;
                    }
                }
                return result;
            }
        }
    }
}