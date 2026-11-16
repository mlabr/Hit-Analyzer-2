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

        [ObservableProperty]
        private bool showTargetPreview;

        internal ShootingSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession,
            RecordingService recording)
        {
            _shootingSession = shootingSession;
            _recording = recording;
            showTargetPreview = settings.Current.Detection.ShowTargetPreview;
            recording.HitCountChanged += RefreshHitCount;
        }

        private void RefreshHitCount()
        {
            if (Dispatcher.UIThread.CheckAccess())
                OnPropertyChanged(nameof(HitCount));
            else
                Dispatcher.UIThread.Post(RefreshHitCount);
        }

        [RelayCommand]
        private void Reset() => _shootingSession.Reset();
    }
}
