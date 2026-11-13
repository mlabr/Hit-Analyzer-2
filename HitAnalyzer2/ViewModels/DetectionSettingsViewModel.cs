using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class DetectionSettingsViewModel : ViewModelBase
    {
        private readonly Action<DetectionMethod, int> _applyDetection;
        private int _detectionSensitivity;
        private volatile int _saturationPercent;
        private volatile int _contrastPercent;
        private volatile int _brightnessPercent;

        internal DetectionSettingsViewModel(SettingsService settings, Action<DetectionMethod, int> applyDetection)
        {
            _applyDetection = applyDetection;
            selectedMethod = settings.Current.Detection.Method;
            _detectionSensitivity = settings.Current.Detection.DetectionSensitivity;
            _brightnessPercent = settings.Current.Detection.BrightnessPercent;
            _contrastPercent = settings.Current.Detection.ContrastPercent;
            _saturationPercent = settings.Current.Detection.SaturationPercent;
            showFlares = settings.Current.Detection.ShowFlares;
            showAllowedArea = settings.Current.Detection.ShowAllowedArea;
        }

        public IReadOnlyList<DetectionMethod> Methods { get; } = Array.AsReadOnly(Enum.GetValues<DetectionMethod>());

        [ObservableProperty]
        private DetectionMethod selectedMethod;

        [ObservableProperty]
        private bool showFlares;

        [ObservableProperty]
        private bool showAllowedArea;

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
            }
        }

        partial void OnSelectedMethodChanged(DetectionMethod value) => ApplyDetection();

        private void ApplyDetection()
        {
            _applyDetection(SelectedMethod, DetectionSensitivity);
        }

        internal DetectionSettings GetConfiguration(DetectionSettings current) => current with
        {
            Method = SelectedMethod,
            DetectionSensitivity = DetectionSensitivity,
            ShowFlares = ShowFlares,
            ShowAllowedArea = ShowAllowedArea,
            BrightnessPercent = BrightnessPercent,
            ContrastPercent = ContrastPercent,
            SaturationPercent = SaturationPercent
        };
    }
}
