using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HitAnalyzer2.Services.Networking
{
    internal sealed class NetworkService
    {
        public async Task SendTestAsync(NetworkingSettings settings)
        {
            var destination = new IPEndPoint(IPAddress.Parse(settings.DestinationAddress), settings.DestinationPort);
            var message = Encoding.UTF8.GetBytes(HipMessages.CreateTest(settings.Sid));

            using var client = new UdpClient(destination.AddressFamily);
            await client.SendAsync(message, message.Length, destination);
        }
    }
}
