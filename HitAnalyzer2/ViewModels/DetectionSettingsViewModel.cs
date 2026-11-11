using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class DetectionSettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _settings;
        private readonly Action<DetectionMethod, int> _applyDetection;
        private int _detectionSensitivity;

        internal DetectionSettingsViewModel(SettingsService settings, Action<DetectionMethod, int> applyDetection)
        {
            _settings = settings;
            _applyDetection = applyDetection;
            selectedMethod = settings.Current.Detection.Method;
            _detectionSensitivity = settings.Current.Detection.DetectionSensitivity;
            showFlares = settings.Current.Detection.ShowFlares;
            settingsMessage = settings.LoadError;
        }

        public IReadOnlyList<DetectionMethod> Methods { get; } = Array.AsReadOnly(Enum.GetValues<DetectionMethod>());

        [ObservableProperty]
        private DetectionMethod selectedMethod;

        [ObservableProperty]
        private string? settingsMessage;

        [ObservableProperty]
        private bool showFlares;

        public int DetectionSensitivity
        {
            get => _detectionSensitivity;
            set
            {
                if (SetProperty(ref _detectionSensitivity, Math.Clamp(value, 1, 99)))
                    ApplyDetection();
            }
        }

        partial void OnSelectedMethodChanged(DetectionMethod value) => ApplyDetection();
        partial void OnShowFlaresChanged(bool value) => SettingsMessage = null;

        private void ApplyDetection()
        {
            _applyDetection(SelectedMethod, DetectionSensitivity);
            SettingsMessage = null;
        }

        [RelayCommand]
        private void SaveDetectionSettings()
        {
            try
            {
                _settings.SaveDetection(SelectedMethod, DetectionSensitivity, ShowFlares);
                SettingsMessage = "Nastavení detekce uloženo.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                SettingsMessage = $"Nastavení detekce se nepodařilo uložit. {ex.Message}";
            }
        }
    }
}
