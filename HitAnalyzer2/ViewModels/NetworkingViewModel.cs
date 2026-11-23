using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Services.Networking;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace HitAnalyzer2.ViewModels
{
    public partial class NetworkingViewModel : ViewModelBase
    {
        private readonly NetworkService _network = new();

        [ObservableProperty] private bool _isEnabled;
        [ObservableProperty] private bool _showAdvanced;
        [ObservableProperty] private string? _receivePort;
        [ObservableProperty] private string? _destinationPort;
        [ObservableProperty] private string? _localAddress;
        [ObservableProperty] private string? _destinationAddress;
        [ObservableProperty] private string? _sid;
        [ObservableProperty] private string? _testStatus;

        internal NetworkingViewModel(NetworkingSettings settings)
        {
            IsEnabled = settings.IsEnabled;
            ReceivePort = settings.ReceivePort.ToString(CultureInfo.InvariantCulture);
            DestinationPort = settings.DestinationPort.ToString(CultureInfo.InvariantCulture);
            LocalAddress = settings.LocalAddress;
            DestinationAddress = settings.DestinationAddress;
            Sid = settings.Sid;
        }

        internal NetworkingSettings GetConfiguration()
        {
            return new NetworkingSettings
            {
                IsEnabled = IsEnabled,
                ReceivePort = ParsePort(ReceivePort, "Receive port"),
                DestinationPort = ParsePort(DestinationPort, "Destination port"),
                LocalAddress = ParseAddress(LocalAddress, "Local IP address"),
                DestinationAddress = ParseAddress(DestinationAddress, "Destination IP address"),
                Sid = ParseSid()
            };
        }

        [RelayCommand]
        private async Task SendTestAsync()
        {
            if (!IsEnabled)
                return;

            try
            {
                var settings = GetConfiguration();
                await _network.SendTestAsync(settings);
                TestStatus = "UDP test message sent. Check the receiving application.";
            }
            catch (JsonException ex)
            {
                TestStatus = ex.Message;
            }
            catch (SocketException ex)
            {
                TestStatus = $"Could not send UDP test message. {ex.Message}";
            }
        }

        private static int ParsePort(string? value, string label)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
                port is < 1 or > 65535)
                throw new JsonException($"{label} must be between 1 and 65535.");

            return port;
        }

        private static string ParseAddress(string? value, string label)
        {
            if (!IPAddress.TryParse(value?.Trim(), out var address))
                throw new JsonException($"{label} must be a valid IP address.");

            return address.ToString();
        }

        private string ParseSid()
        {
            if (Sid is not { Length: 8 } sid || !sid.All(char.IsAsciiDigit))
                throw new JsonException("SID must contain exactly eight digits.");

            return sid;
        }
    }
}
