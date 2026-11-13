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
        private volatile int _saturationPercent;
        private volatile int _contrastPercent;
        private volatile int _brightnessPercent;

        internal DetectionSettingsViewModel(SettingsService settings, Action<DetectionMethod, int> applyDetection)
        {
            _settings = settings;
            _applyDetection = applyDetection;
            selectedMethod = settings.Current.Detection.Method;
            _detectionSensitivity = settings.Current.Detection.DetectionSensitivity;
            _brightnessPercent = settings.Current.Detection.BrightnessPercent;
            _contrastPercent = settings.Current.Detection.ContrastPercent;
            _saturationPercent = settings.Current.Detection.SaturationPercent;
            showFlares = settings.Current.Detection.ShowFlares;
            showAllowedArea = settings.Current.Detection.ShowAllowedArea;
            showAimPointPreview = settings.Current.Detection.ShowAimPointPreview;
            settingsMessage = settings.LoadError;
        }

        public IReadOnlyList<DetectionMethod> Methods { get; } = Array.AsReadOnly(Enum.GetValues<DetectionMethod>());

        [ObservableProperty]
        private DetectionMethod selectedMethod;

        [ObservableProperty]
        private string? settingsMessage;

        [ObservableProperty]
        private bool showFlares;

        [ObservableProperty]
        private bool showAllowedArea;

        [ObservableProperty]
        private bool showAimPointPreview;

        public int DetectionSensitivity
        {
            get => _detectionSensitivity;
            set
            {
                if (SetProperty(ref _detectionSensitivity, Math.Clamp(value, 1, 99)))
                    ApplyDetection();
            }
        }

        public int BrightnessPercent
        {
            get => _brightnessPercent;
            set
            {
                int percent = Math.Clamp(value, 0, 100);
                if (percent == _brightnessPercent)
                    return;

                _brightnessPercent = percent;
                OnPropertyChanged(nameof(BrightnessPercent));
                SettingsMessage = null;
            }
        }

        public int ContrastPercent
        {
            get => _contrastPercent;
            set
            {
                int percent = Math.Clamp(value, 0, 100);
                if (percent == _contrastPercent)
                    return;

                _contrastPercent = percent;
                OnPropertyChanged(nameof(ContrastPercent));
                SettingsMessage = null;
            }
        }

        public int SaturationPercent
        {
            get => _saturationPercent;
            set
            {
                int percent = Math.Clamp(value, 0, 100);
                if (percent == _saturationPercent)
                    return;

                _saturationPercent = percent;
                OnPropertyChanged(nameof(SaturationPercent));
                SettingsMessage = null;
            }
        }

        partial void OnSelectedMethodChanged(DetectionMethod value) => ApplyDetection();
        partial void OnShowFlaresChanged(bool value) => SettingsMessage = null;
        partial void OnShowAllowedAreaChanged(bool value) => SettingsMessage = null;
        partial void OnShowAimPointPreviewChanged(bool value) => SettingsMessage = null;

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
                _settings.SaveDetection(SelectedMethod, DetectionSensitivity, ShowFlares, ShowAllowedArea,
                    ShowAimPointPreview, BrightnessPercent, ContrastPercent, SaturationPercent);
                SettingsMessage = "Nastavení detekce uloženo.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                SettingsMessage = $"Nastavení detekce se nepodařilo uložit. {ex.Message}";
            }
        }
    }
}
