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

namespace Test1.Services
{
    public class P2PService
    {

        public static async Task SendUDPmsg(string ip, int port, string prefix, string msg = "None")
        {
            UdpClient udpClient = new UdpClient(0); // Explicit random port
            IPEndPoint peerEndpoint = new IPEndPoint(IPAddress.Parse(ip), port);
            var message = Encoding.UTF8.GetBytes($"{prefix}|{msg}");
            var sentBytes = await udpClient.SendAsync(message, message.Length, peerEndpoint);
        }
    
        public static async Task PunchPeers(List<OnlineNode>nodes)
        {
            
            await SendUDPmsg("4.188.232.157", 12345, "PUNCHPEERS", JsonConvert.SerializeObject(nodes));
            Thread.Sleep(5000);

        }
    }
}
