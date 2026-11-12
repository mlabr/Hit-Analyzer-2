using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Services.Shooting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class WeaponSettingsViewModel : ViewModelBase
    {
        public sealed class WeaponOption
        {
            internal WeaponOption(string fileName, WeaponProfile profile)
            {
                FileName = fileName;
                Profile = profile;
            }

            public string Label => $"{Profile.Name} ({FileName})";
            internal string FileName { get; }
            internal WeaponProfile Profile { get; set; }
        }

        private readonly SettingsService _settings;
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

        [ObservableProperty]
        private string? offsetSettingsMessage;

        internal WeaponSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession, WeaponHandler weaponHandler)
        {
            _settings = settings;
            _shootingSession = shootingSession;
            _weaponHandler = weaponHandler;
            (offsetX, offsetY) = weaponHandler.Offset;
            var weapon = settings.InitialWeapon;
            selectedTrigger = weapon.Trigger;
            _shootingSession.SetTrigger(weapon.Trigger);
            _shootingSession.NexDetectionInterval = weapon.NexDetectionInterval;
            settingsMessage = settings.LoadError;
            Weapons = LoadWeapons();
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

        public IReadOnlyList<WeaponOption> Weapons { get; }
        public IReadOnlyList<HitTrigger> Triggers { get; } = Array.AsReadOnly(Enum.GetValues<HitTrigger>());

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveWeaponCommand))]
        private WeaponOption? selectedWeapon;

        [ObservableProperty]
        private HitTrigger selectedTrigger;

        [ObservableProperty]
        private string? settingsMessage;

        private IReadOnlyList<WeaponOption> LoadWeapons()
        {
            var weapons = new List<WeaponOption>();
            try
            {
                var paths = Directory.GetFiles(_settings.WeaponsDirectory, "*.json");
                Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
                foreach (var path in paths)
                {
                    try
                    {
                        weapons.Add(new WeaponOption(Path.GetFileName(path), SettingsService.LoadWeaponProfile(path)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        // Unreadable or invalid files are skipped so valid profiles can still be selected.
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                return Array.Empty<WeaponOption>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SettingsMessage ??= $"Profily zbraní se nepodařilo načíst. {ex.Message}";
            }

            return weapons.AsReadOnly();
        }

        partial void OnSelectedWeaponChanged(WeaponOption? value)
        {
            if (value is null)
                return;

            var profile = value.Profile;
            SelectedTrigger = profile.Trigger;
            NexDetectionInterval = profile.NexDetectionInterval;
            OffsetX = profile.OffsetX;
            OffsetY = profile.OffsetY;
            SettingsMessage = null;
            OffsetSettingsMessage = null;
        }

        partial void OnSelectedTriggerChanged(HitTrigger value)
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
            OffsetSettingsMessage = null;
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

        private bool CanSaveWeapon() => SelectedWeapon is not null;

        [RelayCommand(CanExecute = nameof(CanSaveWeapon))]
        private void SaveWeapon()
        {
            if (SelectedWeapon is not { } weapon)
                return;

            var profile = weapon.Profile with
            {
                Trigger = SelectedTrigger,
                NexDetectionInterval = NexDetectionInterval,
                OffsetX = OffsetX,
                OffsetY = OffsetY
            };
            try
            {
                _settings.SaveWeapon(weapon.FileName, profile);
                weapon.Profile = profile;
                OffsetSettingsMessage = "Nastavení zbraně uloženo.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                OffsetSettingsMessage = $"Nastavení zbraně se nepodařilo uložit. {ex.Message}";
            }
        }
    }
}
