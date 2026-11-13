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
        private readonly SettingsService _settings;
        private readonly ShootingSessionService _shootingSession;

        [ObservableProperty]
        private int hitCount;

        [ObservableProperty]
        private bool showAimPointPreview;

        [ObservableProperty]
        private string? previewSettingsError;

        internal ShootingSettingsViewModel(SettingsService settings, ShootingSessionService shootingSession)
        {
            _settings = settings;
            _shootingSession = shootingSession;
            showAimPointPreview = settings.Current.Detection.ShowAimPointPreview;
            shootingSession.LaserPositionEvaluated += (position, _, _) =>
            {
                if (position.HasValue)
                    Dispatcher.UIThread.Post(() => HitCount++);
            };
        }

        partial void OnShowAimPointPreviewChanged(bool value)
        {
            try
            {
                _settings.SaveAimPointPreview(value);
                PreviewSettingsError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                PreviewSettingsError = $"Volbu náhledu se nepodařilo uložit. {ex.Message}";
            }
        }

        [RelayCommand]
        private void ResetHitCount()
        {
            _shootingSession.ClearTargetHit();
            HitCount = 0;
        }
    }
}
