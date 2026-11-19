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
        private readonly DispatcherTimer _sessionDisplayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
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
            set
            {
                if (SetProperty(ref _shootingSession.SessionLimits.IsTimeLimitEnabled, value))
                    OnPropertyChanged(nameof(SessionElapsedText));
            }
        }

        public int TimeLimitMinutes
        {
            get => _shootingSession.SessionLimits.TimeLimitSecondsInitial / 60;
            set => SetTimeLimit(Math.Max(0, value) * 60L + TimeLimitSecondsPart);
        }

        public bool Countdown
        {
            get => countdown;
            set
            {
                if (SetProperty(ref countdown, value))
                    OnPropertyChanged(nameof(SessionElapsedText));
            }
        }

        private bool countdown;

        public int TimeLimitSecondsPart
        {
            get => _shootingSession.SessionLimits.TimeLimitSecondsInitial % 60;
            set => SetTimeLimit(TimeLimitMinutes * 60L + Math.Clamp(value, 0, 59));
        }

        private void SetTimeLimit(long seconds)
        {
            _shootingSession.SessionLimits.TimeLimitSecondsInitial = (int)Math.Clamp(seconds, 1, int.MaxValue);
            OnPropertyChanged(nameof(TimeLimitMinutes));
            OnPropertyChanged(nameof(TimeLimitSecondsPart));
            OnPropertyChanged(nameof(SessionElapsedText));
        }

        internal ShootingSettings GetConfiguration(ShootingSettings current) => current with
        {
            IsHitLimitEnable = IsHitLimitEnabled,
            HitLimit = HitCountInitial,
            IsTimeLimitEnable = IsTimeLimitEnabled,
            Countdown = Countdown,
            TimeLimitSeconds = _shootingSession.SessionLimits.TimeLimitSecondsInitial
        };

        public int HitCount => _recording.GetHitRecordCount();
        public string SessionElapsedText
        {
            get
            {
                if (!IsTimeLimitEnabled)
                    return "--:--";

                var session = _shootingSession.GetSessionStatus();
                long seconds = (long)session.Elapsed.TotalSeconds;
                if (Countdown)
                {
                    var limit = session.CanStart
                        ? TimeSpan.FromSeconds(_shootingSession.SessionLimits.TimeLimitSecondsInitial)
                        : session.TimeLimit;
                    if (limit is null)
                        return "--:--";

                    seconds = (long)Math.Ceiling((limit.Value - session.Elapsed).TotalSeconds);
                }
                return $"{seconds / 60:00}:{seconds % 60:00}";
            }
        }

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
            countdown = settings.Current.Shooting.Countdown;
            _realDistanceMeters = settings.Current.Target.RealDistanceMeters;
            _simulatedDistanceMeters = Math.Max(_realDistanceMeters, settings.Current.Target.SimulatedDistanceMeters);
            recording.HitCountChanged += RefreshHitStatistics;
            _sessionDisplayTimer.Tick += (_, _) => OnPropertyChanged(nameof(SessionElapsedText));
            shootingSession.SessionEnded += () => Dispatcher.UIThread.Post(RefreshSessionStatus);
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

        private bool CanStartSession() => _shootingSession.GetSessionStatus().CanStart;

        [RelayCommand(CanExecute = nameof(CanStartSession))]
        private void StartSession()
        {
            _shootingSession.StartSession();
            RefreshSessionStatus();
        }

        [RelayCommand]
        private void Reset()
        {
            _shootingSession.Reset();
            RefreshSessionStatus();
        }

        private void RefreshSessionStatus()
        {
            if (_shootingSession.GetSessionStatus().IsRunning)
                _sessionDisplayTimer.Start();
            else
                _sessionDisplayTimer.Stop();

            StartSessionCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SessionElapsedText));
        }
    }
}
