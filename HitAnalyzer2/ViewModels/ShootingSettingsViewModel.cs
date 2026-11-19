using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Services.Recording;
using HitAnalyzer2.Services.Shooting;
using System;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class ShootingSettingsViewModel : ViewModelBase
    {
        private readonly ShootingSessionService _shootingSession;
        private readonly RecordingService _recording;
        private readonly Action _saveConfiguration;
        private decimal _realDistanceMeters;
        private decimal _simulatedDistanceMeters;

        public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];

        [ObservableProperty]
        private int selectedThemeIndex;

        partial void OnSelectedThemeIndexChanged(int value)
        {
            if (value >= 0 && value < ThemeOptions.Length)
            {
                App.ApplyTheme((ThemePreference)value);
                _saveConfiguration();
            }
        }

        internal ThemePreference SelectedTheme => (ThemePreference)SelectedThemeIndex;

        public decimal RealDistanceMeters
        {
            get => _realDistanceMeters;
            set
            {
                if (value <= 0)
                    return;
                if (SetProperty(ref _realDistanceMeters, value))
                    SimulatedDistanceMeters = Math.Max(_simulatedDistanceMeters, value);
            }
        }

        public decimal SimulatedDistanceMeters
        {
            get => _simulatedDistanceMeters;
            set => SetProperty(ref _simulatedDistanceMeters, Math.Max(value, _realDistanceMeters));
        }

        [ObservableProperty]
        private bool showAllHits;

        public bool IsHitLimitEnabled
        {
            get => _shootingSession.SessionLimits.IsHitLimitEnabled;
            set => SetProperty(ref _shootingSession.SessionLimits.IsHitLimitEnabled, value);
        }

        public int HitCountInitial
        {
            get => _shootingSession.SessionLimits.HitCountInitial;
            set => SetProperty(ref _shootingSession.SessionLimits.HitCountInitial, Math.Max(0, value));
        }

        public bool IsTimeLimitEnabled
        {
            get => _shootingSession.SessionLimits.IsTimeLimitEnabled;
            set => SetProperty(ref _shootingSession.SessionLimits.IsTimeLimitEnabled, value);
        }

        public int TimeLimitMinutes
        {
            get => _shootingSession.SessionLimits.TimeLimitSeconds / 60;
            set => SetTimeLimit(Math.Max(0, value) * 60L + TimeLimitSecondsPart);
        }

        public int TimeLimitSecondsPart
        {
            get => _shootingSession.SessionLimits.TimeLimitSeconds % 60;
            set => SetTimeLimit(TimeLimitMinutes * 60L + Math.Clamp(value, 0, 59));
        }

        private void SetTimeLimit(long seconds)
        {
            _shootingSession.SessionLimits.TimeLimitSeconds = (int)Math.Clamp(seconds, 1, int.MaxValue);
            OnPropertyChanged(nameof(TimeLimitMinutes));
            OnPropertyChanged(nameof(TimeLimitSecondsPart));
        }

        internal ShootingSettings GetConfiguration(ShootingSettings current) => current with
        {
            IsHitLimitEnable = IsHitLimitEnabled,
            HitLimit = HitCountInitial,
            IsTimeLimitEnable = IsTimeLimitEnabled,
            TimeLimitSeconds = _shootingSession.SessionLimits.TimeLimitSeconds
        };

        public int HitCount => _recording.GetHitRecordCount();
        public double LastHitScore => _recording.GetLastHitScore();
        public double TotalScore => _recording.GetTotalScore();
        public string LastHitDistanceText => _recording.GetLastHitDistanceFromCenterMm() is { } distance
            ? $"Last hit distance from center: {distance:F2} mm"
            : "Last hit distance from center: —";

        internal ShootingSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession,
            RecordingService recording, Action saveConfiguration)
        {
            _shootingSession = shootingSession;
            _recording = recording;
            _saveConfiguration = saveConfiguration;
            selectedThemeIndex = (int)settings.Current.Theme;
            _realDistanceMeters = settings.Current.Target.RealDistanceMeters;
            _simulatedDistanceMeters = Math.Max(_realDistanceMeters, settings.Current.Target.SimulatedDistanceMeters);
            recording.HitCountChanged += RefreshHitStatistics;
        }

        private void RefreshHitStatistics()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                OnPropertyChanged(nameof(HitCount));
                OnPropertyChanged(nameof(LastHitScore));
                OnPropertyChanged(nameof(TotalScore));
                OnPropertyChanged(nameof(LastHitDistanceText));
            }
            else
                Dispatcher.UIThread.Post(RefreshHitStatistics);
        }

        [RelayCommand]
        private void Reset() => _shootingSession.Reset();
    }
}
