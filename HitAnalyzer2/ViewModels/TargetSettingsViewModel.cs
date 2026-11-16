using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HitAnalyzer2.Models;
using HitAnalyzer2.Repository;
using HitAnalyzer2.Services.Target;
using System;
using System.Collections.Generic;
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
        public string Label => $"{Target.TargetName} ({FileName})";
    }

    public partial class TargetSettingsViewModel : ViewModelBase
    {
        private readonly TargetCalibrationService _calibration;

        public IReadOnlyList<TargetOption> Targets { get; }
        public string? LoadMessage { get; }
        public string ScaleText => _calibration.Settings.MmToPixel is { } scale
            ? $"MmToPixel: {scale:0.######} mm/px"
            : "MmToPixel: —";

        [ObservableProperty]
        private TargetOption? selectedTarget;

        internal TargetSettingsViewModel(TargetFileRepository repository, TargetCalibrationService calibration,
            string? selectedFileName)
        {
            _calibration = calibration;
            Targets = repository.LoadTargets(out var loadError)
                .Select(item => new TargetOption(item.FileName, item.Target)).ToList().AsReadOnly();
            LoadMessage = loadError ?? (Targets.Count == 0 ? "No targets found in Data/Targets." : null);
            SelectedTarget = FindTarget(selectedFileName) ?? FindTarget("air_rifle_cz.json") ?? Targets.FirstOrDefault();
            _calibration.PreviewGeometryChanged += () =>
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(ScaleText)));
        }

        private TargetOption? FindTarget(string? fileName) =>
            Targets.FirstOrDefault(option => string.Equals(option.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        partial void OnSelectedTargetChanged(TargetOption? value)
        {
            _calibration.SetTarget(value?.Target ?? new TargetModel());
            OnPropertyChanged(nameof(ScaleText));
        }
    }
}
