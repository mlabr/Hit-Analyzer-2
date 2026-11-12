using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Services.Shooting;
using System;
using System.IO;
using System.Text.Json;

namespace HitAnalyzer2.ViewModels
{
    public sealed partial class ShootingSettingsViewModel : ViewModelBase
    {
        private readonly WeaponHandler _weaponHandler;

        public int MinimumOffset => WeaponHandler.MinimumOffset;
        public int MaximumOffset => WeaponHandler.MaximumOffset;

        [ObservableProperty]
        private int offsetX;

        [ObservableProperty]
        private int offsetY;

        [ObservableProperty]
        private string? offsetSettingsMessage;

        [ObservableProperty]
        private int hitCount;

        internal ShootingSettingsViewModel(ShootingSessionService shootingSession, WeaponHandler weaponHandler)
        {
            _weaponHandler = weaponHandler;
            (offsetX, offsetY) = weaponHandler.Offset;
            shootingSession.LaserPositionEvaluated += (position, _) =>
            {
                if (position.HasValue)
                    Dispatcher.UIThread.Post(() => HitCount++);
            };
        }

        partial void OnOffsetXChanged(int value) => ApplyOffset();

        partial void OnOffsetYChanged(int value) => ApplyOffset();

        private void ApplyOffset()
        {
            _weaponHandler.SetOffset(OffsetX, OffsetY);
            OffsetSettingsMessage = null;
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
        private void ResetHitCount() => HitCount = 0;
    }
}
