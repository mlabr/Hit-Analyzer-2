using HitAnalyzer2.Services.Shooting;

namespace HitAnalyzer2.ViewModels
{
    public sealed class WeaponSettingsViewModel : ViewModelBase
    {
        private readonly ShootingSessionService _shootingSession;

        internal WeaponSettingsViewModel(ShootingSessionService shootingSession)
        {
            _shootingSession = shootingSession;
        }

        public int NexDetectionInterval
        {
            get => _shootingSession.NexDetectionInterval;
            set
            {
                int previous = _shootingSession.NexDetectionInterval;
                _shootingSession.NexDetectionInterval = value;
                if (previous != _shootingSession.NexDetectionInterval)
                    OnPropertyChanged();
            }
        }
    }
}
