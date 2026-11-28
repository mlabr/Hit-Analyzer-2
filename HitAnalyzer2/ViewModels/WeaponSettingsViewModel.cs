using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Repository;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Target;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class WeaponSettingsViewModel : ViewModelBase
    {
        private readonly ShootingSessionService _shootingSession;

        private readonly WeaponHandler _weaponHandler;
        private readonly TargetCalibrationService _calibration;
        private const decimal MillimetersPerInch = 25.4m;
        private bool _showCaliberInInches;

        public int MinimumOffset => WeaponHandler.MinimumOffset;
        public int MaximumOffset => WeaponHandler.MaximumOffset;
        internal ActiveWeapon ActiveWeapon => _weaponHandler.Current;
        public string CaliberText
        {
            get
            {
                decimal diameterMm = ActiveWeapon.CaliberDiameterMm;
                decimal displayedDiameter = _showCaliberInInches ? diameterMm / MillimetersPerInch : diameterMm;
                return displayedDiameter.ToString("0.############################", CultureInfo.CurrentCulture);
            }
        }

        public string CaliberUnit => _showCaliberInInches ? "in" : "mm";

        public int OffsetX
        {
            get => ActiveWeapon.Settings.OffsetX;
            set => SetOffset(value, OffsetY);
        }

        public int OffsetY
        {
            get => ActiveWeapon.Settings.OffsetY;
            set => SetOffset(OffsetX, value);
        }

        public int DisplayOffsetY => -OffsetY;
        public string OffsetXMmText => FormatOffsetMm(OffsetX);
        public string OffsetYMmText => FormatOffsetMm(DisplayOffsetY);

        private string FormatOffsetMm(int offset) => _calibration.Settings.MmToPixel is { } scale
            ? $"{offset * scale:F2} mm"
            : "— mm";

        internal WeaponSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession,
            WeaponHandler weaponHandler, WeaponFileRepository weaponProfiles, TargetCalibrationService calibration)
        {
            _shootingSession = shootingSession;
            _weaponHandler = weaponHandler;
            _calibration = calibration;
            calibration.CalibrationGeometryChanged += RefreshOffsetScale;
            var profiles = weaponProfiles.LoadProfileList(out var loadError);
            Weapons = Array.AsReadOnly(profiles
                .Select(profile => new WeaponProfileItem(profile.FileName, profile.Profile))
                .ToArray());
            if (loadError is not null)
                SettingsMessage ??= $"Could not load weapon profiles. {loadError}";

            if (settings.Current.SelectedWeapon is { } savedWeapon)
            {
                foreach (var option in Weapons)
                {
                    if (option.FileName == savedWeapon.FileName)
                    {
                        SelectedWeapon = option;
                        break;
                    }
                }

                if (SelectedWeapon is null)
                    SettingsMessage ??= $"Could not load weapon profile {savedWeapon.FileName}.";
            }
        }

        public IReadOnlyList<WeaponProfileItem> Weapons { get; }
        public IReadOnlyList<TriggerTypeEnum> Triggers { get; } = Array.AsReadOnly(Enum.GetValues<TriggerTypeEnum>());

        [ObservableProperty]
        private WeaponProfileItem? selectedWeapon;

        public TriggerTypeEnum SelectedTrigger
        {
            get => ActiveWeapon.Settings.Trigger;
            set
            {
                if (value == SelectedTrigger)
                    return;

                _shootingSession.SetTrigger(value);
                OnPropertyChanged();
                SettingsMessage = null;
            }
        }

        [ObservableProperty]
        private string? settingsMessage;

        partial void OnSelectedWeaponChanged(WeaponProfileItem? value)
        {
            _shootingSession.SelectWeapon(value?.FileName, value?.Profile);
            _showCaliberInInches = false;
            OnPropertyChanged(nameof(CaliberText));
            OnPropertyChanged(nameof(CaliberUnit));
            if (value is null)
                return;

            OnPropertyChanged(nameof(SelectedTrigger));
            OnPropertyChanged(nameof(NexDetectionInterval));
            NotifyOffsetChanged();
            SettingsMessage = null;
        }

        public int NexDetectionInterval
        {
            get => ActiveWeapon.Settings.NexDetectionInterval;
            set
            {
                int previous = NexDetectionInterval;
                _weaponHandler.SetDetectionInterval(value);
                if (previous != NexDetectionInterval)
                {
                    OnPropertyChanged();
                    SettingsMessage = null;
                }
            }
        }

        internal void CommitCaliber(string? text)
        {
            if (text == CaliberText)
                return;

            const NumberStyles style = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            if ((decimal.TryParse(text, style, CultureInfo.CurrentCulture, out var diameter) ||
                decimal.TryParse(text, style, CultureInfo.InvariantCulture, out diameter)) && diameter >= 0)
            {
                bool enteredInInches = diameter < 1;
                decimal diameterMm = enteredInInches ? diameter * MillimetersPerInch : diameter;
                _weaponHandler.SetCaliber(diameterMm);
                _showCaliberInInches = enteredInInches && ActiveWeapon.CaliberDiameterMm == diameterMm;
                OnPropertyChanged(nameof(CaliberUnit));
            }

            OnPropertyChanged(nameof(CaliberText));
        }

        internal void SetOffset(int x, int y)
        {
            if (x == OffsetX && y == OffsetY)
                return;

            _weaponHandler.SetOffset(x, y);
            NotifyOffsetChanged();
        }

        private void NotifyOffsetChanged()
        {
            OnPropertyChanged(nameof(OffsetX));
            OnPropertyChanged(nameof(OffsetY));
            OnPropertyChanged(nameof(DisplayOffsetY));
            RefreshOffsetScale();
        }

        private void RefreshOffsetScale()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshOffsetScale);
                return;
            }

            OnPropertyChanged(nameof(OffsetXMmText));
            OnPropertyChanged(nameof(OffsetYMmText));
        }

        [RelayCommand]
        private void NudgeOffset(string direction)
        {
            switch (direction)
            {
                case "Left":
                    OffsetX = Math.Max(OffsetX - 1, MinimumOffset);
                    break;
                case "Right":
                    OffsetX = Math.Min(OffsetX + 1, MaximumOffset);
                    break;
                case "Up":
                    OffsetY = Math.Max(OffsetY - 1, MinimumOffset);
                    break;
                case "Down":
                    OffsetY = Math.Min(OffsetY + 1, MaximumOffset);
                    break;
            }
        }

        [RelayCommand]
        private void ResetOffset() => SetOffset(0, 0);

        [RelayCommand]
        private void ToCenter()
        {
            if (_shootingSession.GetOffsetToCenterLastHit() is { } offset)
                SetOffset(offset.X, offset.Y);
        }
    }
}
