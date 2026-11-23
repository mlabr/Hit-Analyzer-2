using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HitAnalyzer2.Services.AppLink
{
    internal sealed class AppLinkService
    {
        public async Task SendTestAsync(AppLinkSettings settings)
        {
            var destination = new IPEndPoint(IPAddress.Parse(settings.DestinationAddress), settings.DestinationPort);
            var message = Encoding.UTF8.GetBytes(HipMessages.GetTest());

            using var client = new UdpClient(destination.AddressFamily);
            await client.SendAsync(message, message.Length, destination);
        }
    }
}
