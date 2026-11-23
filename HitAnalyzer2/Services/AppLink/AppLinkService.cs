using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HitAnalyzer2.Services.AppLink
{
    internal sealed class AppLinkService
    {
        public Task SendTestAsync(AppLinkSettings settings) => SendAsync(settings, HipMessages.GetTest());

        public async Task SendAsync(AppLinkSettings settings, string message)
        {
            var destination = new IPEndPoint(IPAddress.Parse(settings.DestinationAddress), settings.DestinationPort);
            var payload = Encoding.UTF8.GetBytes(message);

            using var client = new UdpClient(destination.AddressFamily);
            await client.SendAsync(payload, payload.Length, destination);
        }
    }
}
