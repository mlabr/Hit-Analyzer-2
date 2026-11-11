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

        internal WeaponSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession)
        {
            _settings = settings;
            _shootingSession = shootingSession;
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
