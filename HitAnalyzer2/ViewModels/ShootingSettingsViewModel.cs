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
        private readonly Func<string?> _getWeaponName;
        private readonly DispatcherTimer _sessionDisplayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _sessionCompleteTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
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

        [ObservableProperty]
        private string shooter = string.Empty;

        [ObservableProperty]
        private bool showSmallestHitGroup;
        public bool IsEnhancedR80Available => false;

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
                {
                    OnPropertyChanged(nameof(SessionElapsedText));
                    OnPropertyChanged(nameof(CanUseCountdown));
                }
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
            Shooter = Shooter,
            IsHitLimitEnable = IsHitLimitEnabled,
            HitLimit = HitCountInitial,
            IsTimeLimitEnable = IsTimeLimitEnabled,
            Countdown = Countdown,
            ShowSmallestHitGroup = ShowSmallestHitGroup,
            TimeLimitSeconds = _shootingSession.SessionLimits.TimeLimitSecondsInitial
        };

        public bool IsMonoscope => _shootingSession.IsMonoscope;
        public bool CanUseCountdown => !IsMonoscope && IsTimeLimitEnabled;
        public int HitCount => IsMonoscope ? _shootingSession.MonoscopeHitCount : _recording.GetHitRecordCount();
        public int StartCountdown => _shootingSession.GetSessionStatus().StartCountdown;
        public bool IsStartCountdownVisible => StartCountdown > 0;
        public bool IsTabSwitchingEnabled
        {
            get
            {
                var status = _shootingSession.GetSessionStatus();
                return !status.IsRunning && status.StartCountdown == 0;
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSessionComplete))]
        [NotifyPropertyChangedFor(nameof(ShowScoredSessionResults))]
        private string? sessionCompletionReason;

        [ObservableProperty]
        private bool isSessionCompleteOverlayVisible;

        public bool IsSessionComplete => SessionCompletionReason is not null;
        public bool ShowScoredSessionResults => IsSessionComplete && !IsMonoscope;

        public string SessionElapsedText
        {
            get
            {
                if (IsMonoscope || !IsTimeLimitEnabled)
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
            RecordingService recording, Action saveConfiguration, Func<string?> getWeaponName)
        {
            _shootingSession = shootingSession;
            _recording = recording;
            _saveConfiguration = saveConfiguration;
            _getWeaponName = getWeaponName;
            selectedThemeIndex = (int)settings.Current.Theme;
            countdown = settings.Current.Shooting.Countdown;
            shooter = settings.Current.Shooting.Shooter ?? string.Empty;
            showSmallestHitGroup = settings.Current.Shooting.ShowSmallestHitGroup;
            _realDistanceMeters = settings.Current.Target.RealDistanceMeters;
            _simulatedDistanceMeters = Math.Max(_realDistanceMeters, settings.Current.Target.SimulatedDistanceMeters);
            recording.HitCountChanged += RefreshHitStatistics;
            shootingSession.MonoscopeHitCountChanged += RefreshHitStatistics;
            _sessionDisplayTimer.Tick += (_, _) => OnPropertyChanged(nameof(SessionElapsedText));
            _sessionCompleteTimer.Tick += (_, _) =>
            {
                _sessionCompleteTimer.Stop();
                IsSessionCompleteOverlayVisible = false;
            };
            shootingSession.SessionStatusChanged += () => Dispatcher.UIThread.Post(RefreshSessionStatus);
        }

        internal void RefreshTargetMode()
        {
            OnPropertyChanged(nameof(IsMonoscope));
            OnPropertyChanged(nameof(CanUseCountdown));
            OnPropertyChanged(nameof(ShowScoredSessionResults));
            OnPropertyChanged(nameof(HitCount));
            OnPropertyChanged(nameof(SessionElapsedText));
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
            _shootingSession.StartSession(new(Shooter, _getWeaponName(), RealDistanceMeters, SimulatedDistanceMeters));
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
            var status = _shootingSession.GetSessionStatus();
            if (status.IsRunning && !IsMonoscope)
                _sessionDisplayTimer.Start();
            else
                _sessionDisplayTimer.Stop();

            var reason = status.EndReason switch
            {
                ShootingSessionService.SessionEndReason.TimeLimit => "Time limit reached",
                ShootingSessionService.SessionEndReason.HitLimit => "Hit limit reached",
                _ => null
            };
            if (reason != SessionCompletionReason)
            {
                SessionCompletionReason = reason;
                _sessionCompleteTimer.Stop();
                IsSessionCompleteOverlayVisible = reason is not null;
                if (IsSessionCompleteOverlayVisible)
                    _sessionCompleteTimer.Start();
            }

            StartSessionCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SessionElapsedText));
            OnPropertyChanged(nameof(StartCountdown));
            OnPropertyChanged(nameof(IsStartCountdownVisible));
            OnPropertyChanged(nameof(IsTabSwitchingEnabled));
        }
    }
}
