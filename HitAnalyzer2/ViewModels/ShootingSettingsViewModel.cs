using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Services.Shooting;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class ShootingSettingsViewModel : ViewModelBase
    {
        private readonly ShootingSessionService _shootingSession;

        [ObservableProperty]
        private int hitCount;

        [ObservableProperty]
        private bool showTargetPreview;

        internal ShootingSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession)
        {
            _shootingSession = shootingSession;
            showTargetPreview = settings.Current.Detection.ShowTargetPreview;
            shootingSession.LaserPositionEvaluated += (position, _, _) =>
            {
                if (position.HasValue)
                    Dispatcher.UIThread.Post(() => HitCount++);
            };
        }

        [RelayCommand]
        private void ResetHitCount()
        {
            _shootingSession.ClearTargetHit();
            HitCount = 0;
        }
    }
}
