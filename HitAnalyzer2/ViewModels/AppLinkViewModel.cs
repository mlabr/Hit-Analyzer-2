using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Models;
using HitAnalyzer2.Services.AppLink;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace HitAnalyzer2.ViewModels
{
    public partial class AppLinkViewModel : ViewModelBase
    {
        private readonly AppLinkService _appLinkService = new();

        [ObservableProperty] private bool _isEnabled;
        [ObservableProperty] private bool _showAdvanced;
        [ObservableProperty] private string? _receivePort;
        [ObservableProperty] private string? _destinationPort;
        [ObservableProperty] private string? _localAddress;
        [ObservableProperty] private string? _destinationAddress;
        [ObservableProperty] private string? _sid;
        [ObservableProperty] private string? _testStatus;

        internal AppLinkViewModel(AppLinkSettings settings)
        {
            IsEnabled = settings.IsEnabled;
            ReceivePort = settings.ReceivePort.ToString(CultureInfo.InvariantCulture);
            DestinationPort = settings.DestinationPort.ToString(CultureInfo.InvariantCulture);
            LocalAddress = settings.LocalAddress;
            DestinationAddress = settings.DestinationAddress;
            Sid = settings.Sid;
        }

        partial void OnSidChanged(string? value)
        {
            if (value is { Length: 8 } && value.All(char.IsAsciiDigit))
                HipMessages.Sid = value;
        }

        internal AppLinkSettings GetConfiguration()
        {
            return new AppLinkSettings
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
                await _appLinkService.SendTestAsync(settings);
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

        internal async Task SendMonoscopeHitAsync(Position? position)
        {
            if (!IsEnabled)
                return;

            try
            {
                var settings = GetConfiguration();
                var message = position is null
                    ? HipMessages.GetMonoscopeOutOfRange()
                    : HipMessages.GetMonoscopeHit(position.X, position.Y);
                await _appLinkService.SendAsync(settings, message);
            }
            catch (System.Exception ex) when (ex is JsonException or SocketException)
            {
                TestStatus = $"Could not send HIP message. {ex.Message}";
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
