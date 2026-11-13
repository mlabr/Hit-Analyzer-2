using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Repository;
using HitAnalyzer2.Services.Shooting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class WeaponSettingsViewModel : ViewModelBase
    {
        private readonly ShootingSessionService _shootingSession;

        private readonly WeaponHandler _weaponHandler;

        public int MinimumOffset => WeaponHandler.MinimumOffset;
        public int MaximumOffset => WeaponHandler.MaximumOffset;

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
                SettingsMessage ??= $"Profily zbraní se nepodařilo načíst. {loadError}";

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
                    SettingsMessage ??= $"Profil zbraně {savedWeapon.FileName} se nepodařilo načíst.";
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
            ? new(weapon.FileName, (WeaponProfile)GetConfiguration(weapon.Profile))
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
