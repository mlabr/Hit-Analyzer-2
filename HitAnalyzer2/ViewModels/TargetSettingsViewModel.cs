using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HitAnalyzer2.Models;
using HitAnalyzer2.Repository;
using HitAnalyzer2.Services.Target;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HitAnalyzer2.ViewModels
{
    public sealed class TargetOption
    {
        internal TargetOption(string fileName, TargetModel target)
        {
            FileName = fileName;
            Target = target;
        }

        internal string FileName { get; }
        internal TargetModel Target { get; }
        public string Label => $"{Target.TargetName} ({Path.GetFileNameWithoutExtension(FileName)})";
    }

    public partial class TargetSettingsViewModel : ViewModelBase
    {
        private readonly TargetCalibrationService _calibration;
        private int _calibrationZoomIndex;
        private ShootingSettingsViewModel? _shootingSettings;

        [ObservableProperty]
        private bool adjustScaleForDistance;

        partial void OnAdjustScaleForDistanceChanged(bool value) => RefreshScale();

        internal void SetShootingSettings(ShootingSettingsViewModel shootingSettings)
        {
            _shootingSettings = shootingSettings;
            shootingSettings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ShootingSettingsViewModel.RealDistanceMeters) or
                    nameof(ShootingSettingsViewModel.SimulatedDistanceMeters))
                    RefreshScale();
            };
            RefreshScale();
        }

        public int CalibrationZoomIndex
        {
            get => _calibrationZoomIndex;
            set => SetProperty(ref _calibrationZoomIndex, Math.Clamp(value, 0, 2));
        }

        internal TargetSettings GetConfiguration() => new()
        {
            SelectedTargetFileName = SelectedTarget?.FileName,
            CalibrationZoom = 2 << CalibrationZoomIndex,
            AdjustScaleForDistance = AdjustScaleForDistance,
            RealDistanceMeters = _shootingSettings!.RealDistanceMeters,
            SimulatedDistanceMeters = _shootingSettings.SimulatedDistanceMeters,
            Calibration = _calibration.Settings
        };

        public IReadOnlyList<TargetOption> Targets { get; }
        public string? LoadMessage { get; }
        public string CalibratedScaleText
        {
            get
            {
                double factor = AdjustScaleForDistance && _shootingSettings is { } shooting
                    ? (double)(shooting.RealDistanceMeters / shooting.SimulatedDistanceMeters)
                    : 1;
                return $"Average camera scale: {FormatScale(_calibration.Settings.MmToPixel * factor)}";
            }
        }

        [ObservableProperty]
        private TargetOption? selectedTarget;

        internal TargetSettingsViewModel(TargetFileRepository repository, TargetCalibrationService calibration,
            string? selectedFileName, TargetSettings settings)
        {
            _calibration = calibration;
            adjustScaleForDistance = settings.AdjustScaleForDistance;
            _calibrationZoomIndex = settings.CalibrationZoom switch { 4 => 1, 8 => 2, _ => 0 };
            Targets = repository.LoadTargets(out var loadError)
                .Select(item => new TargetOption(item.FileName, item.Target)).ToList().AsReadOnly();
            LoadMessage = loadError ?? (Targets.Count == 0 ? "No targets found in Data/Targets." : null);
            selectedTarget = FindTarget(selectedFileName) ?? FindTarget("air_rifle_cz.json") ?? Targets.FirstOrDefault();
            if (selectedTarget is { } initialTarget)
                _calibration.SetTarget(initialTarget.Target);
            _calibration.CalibrationGeometryChanged += RefreshScale;
        }

        private void RefreshScale()
        {
            if (Dispatcher.UIThread.CheckAccess())
                OnPropertyChanged(nameof(CalibratedScaleText));
            else
                Dispatcher.UIThread.Post(RefreshScale);
        }

        private static string FormatScale(double? scale) => scale is { } value ? $"{value:F3} mm/px" : "—";

        private TargetOption? FindTarget(string? fileName) =>
            Targets.FirstOrDefault(option => string.Equals(option.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        partial void OnSelectedTargetChanged(TargetOption? value)
        {
            if (value is null)
                _calibration.SetTarget(new TargetModel());
            else
                _calibration.ResetCalibration(value.Target);
        }
    }
}
