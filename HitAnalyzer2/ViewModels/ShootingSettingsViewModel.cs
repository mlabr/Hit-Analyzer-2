using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Services.Recording;
using HitAnalyzer2.Services.Shooting;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class ShootingSettingsViewModel : ViewModelBase
    {
        private readonly ShootingSessionService _shootingSession;
        private readonly RecordingService _recording;

        public int HitCount => _recording.GetHitRecordCount();
        public double LastHitScore => _recording.GetLastHitScore();
        public string LastHitDistanceText => _recording.GetLastHitDistanceFromCenterMm() is { } distance
            ? $"Last hit distance from center: {distance:F2} mm"
            : "Last hit distance from center: —";

        [ObservableProperty]
        private bool showTargetPreview;

        internal ShootingSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession,
            RecordingService recording)
        {
            _shootingSession = shootingSession;
            _recording = recording;
            showTargetPreview = settings.Current.Detection.ShowTargetPreview;
            recording.HitCountChanged += RefreshHitStatistics;
        }

        private void RefreshHitStatistics()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                OnPropertyChanged(nameof(HitCount));
                OnPropertyChanged(nameof(LastHitScore));
                OnPropertyChanged(nameof(LastHitDistanceText));
            }
            else
                Dispatcher.UIThread.Post(RefreshHitStatistics);
        }

        [RelayCommand]
        private void Reset() => _shootingSession.Reset();
    }
}
