using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Services.Recording;
using HitAnalyzer2.Services.Rendering;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Target;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HitAnalyzer2.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly SettingsService _settings;
        private readonly ShootingSessionService _shootingSession;
        private readonly DetectionSettings _detectionSettings;
        internal volatile bool IsShootingTabActive = true;

        public CameraSettingsViewModel CameraSettings { get; }
        public CameraPreviewViewModel CameraPreview { get; } = new();
        public DetectionSettingsViewModel DetectionSettings { get; }
        public ShootingSettingsViewModel ShootingSettings { get; }
        public WeaponSettingsViewModel WeaponSettings { get; }
        public TargetSettingsViewModel TargetSettings { get; }

        [ObservableProperty]
        private string? configurationMessage;

        internal MainWindowViewModel(SettingsService settings, DetectionSettingsViewModel detectionSettings,
            WeaponSettingsViewModel weaponSettings, TargetSettingsViewModel targetSettings, ShootingSessionService shootingSession,
            TargetCalibrationService cameraCalibration, RecordingService recording)
        {
            _settings = settings;
            configurationMessage = settings.LoadError;
            DetectionSettings = detectionSettings;
            _detectionSettings = settings.Current.Detection;
            WeaponSettings = weaponSettings;
            TargetSettings = targetSettings;
            _shootingSession = shootingSession;
            ShootingSettings = new ShootingSettingsViewModel(settings, shootingSession, recording);
            var captureSession = new CameraCaptureSession(
                ProcessCameraFrameAsync,
                () =>
                {
                    shootingSession.ClearAimPaths();
                    cameraCalibration.UpdatePreviewGeometry(0, 0);
                    CameraPreview.ClearPreview();
                },
                message => CameraPreview.StatusMessage = message);

            CameraSettings = new CameraSettingsViewModel(settings, captureSession, cameraCalibration);
        }

        internal Task RefreshCamerasAsync() => CameraSettings.RefreshCamerasAsync();

        internal Task StopCameraAsync() => CameraSettings.StopCameraAsync();

        [RelayCommand]
        private void SaveConfiguration()
        {
            var current = _settings.Current;
            var camera = CameraSettings.GetConfiguration(current.Camera);
            var detection = DetectionSettings.GetConfiguration(current.Detection) with
            {
                ShowTargetPreview = ShootingSettings.ShowTargetPreview
            };
            var weapon = WeaponSettings.GetSelectedProfile();
            var configuration = current with
            {
                Camera = camera,
                Detection = detection,
                SelectedWeapon = weapon ?? current.SelectedWeapon,
                SelectedTargetFileName = TargetSettings.SelectedTarget?.FileName ?? current.SelectedTargetFileName,
                LegacyWeapon = weapon is null && current.SelectedWeapon is null
                    ? WeaponSettings.GetConfiguration(current.LegacyWeapon ?? new WeaponSettings())
                    : null
            };

            try
            {
                _settings.SaveConfiguration(configuration, weapon);
                if (weapon is not null && WeaponSettings.SelectedWeapon is { } selected)
                    selected.Profile = weapon.Profile;
                ConfigurationMessage = "Nastavení uloženo.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                ConfigurationMessage = $"Nastavení se nepodařilo uložit. {ex.Message}";
            }
        }

        private async Task ProcessCameraFrameAsync(Mat frame, long capturedAt, double detectedFps, string measuredFps,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CameraSettings.Calibration.UpdatePreviewGeometry(frame.Width, frame.Height);
            using var output = new Mat();
            var pixels = FramePreprocessor.ApplyImageAdjustments(frame, output, DetectionSettings.BrightnessPercent,
                DetectionSettings.ContrastPercent, DetectionSettings.SaturationPercent);
            bool showTargetPreview = IsShootingTabActive && ShootingSettings.ShowTargetPreview;
            var (lastHit, aimPath, followPath, lastTargetHit) =
                _shootingSession.ProcessFrame(pixels, capturedAt, detectedFps, cancellationToken);
            if (showTargetPreview)
            {
                using var target = new Mat(pixels.Rows, pixels.Cols, pixels.Type());
                TargetRenderer.DrawTargetPreview(target, lastTargetHit, CameraSettings.Calibration.Settings);
                await CameraPreview.ShowFrameAsync(target, measuredFps, cancellationToken);
                return;
            }

            int? flareSensitivity = DetectionSettings.ShowFlares ? DetectionSettings.DetectionSensitivity : null;
            bool showAllowedArea = DetectionSettings.ShowAllowedArea;

            if (ReferenceEquals(pixels, frame) &&
                (lastHit.HasValue || flareSensitivity.HasValue || showAllowedArea))
            {
                frame.CopyTo(output);
                pixels = output;
            }

            if (flareSensitivity is { } sensitivity)
                FlareDetectionRenderer.Draw(pixels, sensitivity);

            if (showAllowedArea)
                AllowedAreaRenderer.Draw(pixels, _detectionSettings);

            if (lastHit is { } hit)
                AimTracerRenderer.DrawHitAndTraceIntoFrame(pixels, hit, aimPath, followPath);

            await CameraPreview.ShowFrameAsync(pixels, measuredFps, cancellationToken);
        }
    }
}
