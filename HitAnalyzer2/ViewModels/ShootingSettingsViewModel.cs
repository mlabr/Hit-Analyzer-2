using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HitAnalyzer2.Services.Shooting;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class ShootingSettingsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private int hitCount;

        internal ShootingSettingsViewModel(ShootingSessionService shootingSession)
        {
            shootingSession.LaserPositionEvaluated += (position, _) =>
            {
                if (position.HasValue)
                    Dispatcher.UIThread.Post(() => HitCount++);
            };
        }
    }
}
