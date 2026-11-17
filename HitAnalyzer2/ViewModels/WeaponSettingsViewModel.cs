using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Repository;
using HitAnalyzer2.Services.Shooting;
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
        private const decimal MillimetersPerInch = 25.4m;
        private bool _showCaliberInInches;

        public int MinimumOffset => WeaponHandler.MinimumOffset;
        public int MaximumOffset => WeaponHandler.MaximumOffset;
        public string CaliberText
        {
            get
            {
                decimal diameterMm = _weaponHandler.CaliberDiameterMm;
                decimal displayedDiameter = _showCaliberInInches ? diameterMm / MillimetersPerInch : diameterMm;
                return displayedDiameter.ToString("0.############################", CultureInfo.CurrentCulture);
            }
        }

        public string CaliberUnit => _showCaliberInInches ? "in" : "mm";

        [ObservableProperty]
        private int offsetX;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayOffsetY))]
        private int offsetY;

        public int DisplayOffsetY
        {
            get => -OffsetY;
            set => OffsetY = -value;
        }

        internal WeaponSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession,
            WeaponHandler weaponHandler, WeaponFileRepository weaponProfiles)
        {
            _shootingSession = shootingSession;
            _weaponHandler = weaponHandler;
            (offsetX, offsetY) = weaponHandler.Offset;
            var weapon = settings.InitialWeapon;
            selectedTrigger = weapon.Trigger;
            _shootingSession.SetTrigger(weapon.Trigger);
            _shootingSession.NexDetectionInterval = weapon.NexDetectionInterval;
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

        [ObservableProperty]
        private TriggerTypeEnum selectedTrigger;

        [ObservableProperty]
        private string? settingsMessage;

        partial void OnSelectedWeaponChanged(WeaponProfileItem? value)
        {
            _weaponHandler.SetCaliber(value?.Profile.Caliber ?? 0);
            _showCaliberInInches = false;
            OnPropertyChanged(nameof(CaliberText));
            OnPropertyChanged(nameof(CaliberUnit));
            if (value is null)
                return;

            var profile = value.Profile;
            SelectedTrigger = profile.Trigger;
            NexDetectionInterval = profile.NexDetectionInterval;
            OffsetX = profile.OffsetX;
            OffsetY = profile.OffsetY;
            SettingsMessage = null;
        }

        partial void OnSelectedTriggerChanged(TriggerTypeEnum value)
        {
            _shootingSession.SetTrigger(value);
            SettingsMessage = null;
        }

        public int NexDetectionInterval
        {
            get => _shootingSession.NexDetectionInterval;
            set
            {
                int previous = _shootingSession.NexDetectionInterval;
                _shootingSession.NexDetectionInterval = value;
                if (previous != _shootingSession.NexDetectionInterval)
                {
                    OnPropertyChanged();
                    SettingsMessage = null;
                }
            }
        }

        partial void OnOffsetXChanged(int value) => ApplyOffset();

        partial void OnOffsetYChanged(int value) => ApplyOffset();

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
                _showCaliberInInches = enteredInInches && _weaponHandler.CaliberDiameterMm == diameterMm;
                OnPropertyChanged(nameof(CaliberUnit));
            }

            OnPropertyChanged(nameof(CaliberText));
        }

        private void ApplyOffset()
        {
            _weaponHandler.SetOffset(OffsetX, OffsetY);
        }

        [RelayCommand]
        private void ResetOffset()
        {
            OffsetX = 0;
            OffsetY = 0;
        }

        [RelayCommand]
        private void ToCenter()
        {
        }

        internal SavedWeaponProfile? GetSelectedProfile() => SelectedWeapon is { } weapon
            ? new(weapon.FileName, ((WeaponProfile)GetConfiguration(weapon.Profile)) with
            {
                Caliber = _weaponHandler.CaliberDiameterMm
            })
            : null;

        internal WeaponSettings GetConfiguration(WeaponSettings current) => current with
        {
            Trigger = SelectedTrigger,
            NexDetectionInterval = NexDetectionInterval,
            OffsetX = OffsetX,
            OffsetY = OffsetY
        };
    }
}
