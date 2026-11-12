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
            var weapon = settings.Current.Weapon;
            selectedTrigger = weapon.Trigger;
            _shootingSession.SetTrigger(weapon.Trigger);
            _shootingSession.NexDetectionInterval = weapon.NexDetectionInterval;
            settingsMessage = settings.LoadError;
        }

        public IReadOnlyList<HitTrigger> Triggers { get; } = Array.AsReadOnly(Enum.GetValues<HitTrigger>());

        [ObservableProperty]
        private HitTrigger selectedTrigger;

        [ObservableProperty]
        private string? settingsMessage;

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

        [RelayCommand]
        private void SaveOffset()
        {
            try
            {
                _weaponHandler.SaveOffset();
                OffsetSettingsMessage = "Offset zbraně uložen.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                OffsetSettingsMessage = $"Offset zbraně se nepodařilo uložit. {ex.Message}";
            }
        }

        [RelayCommand]
        private void SaveWeaponSettings()
        {
            try
            {
                _settings.SaveWeapon(SelectedTrigger, NexDetectionInterval);
                SettingsMessage = "Nastavení zbraně uloženo.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                SettingsMessage = $"Nastavení zbraně se nepodařilo uložit. {ex.Message}";
            }
        }
    }
}
