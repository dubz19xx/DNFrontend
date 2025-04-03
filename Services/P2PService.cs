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

        private readonly UDPService _udpService;



        public P2PService(UDPService udpService)

        {

            _udpService = udpService;

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
