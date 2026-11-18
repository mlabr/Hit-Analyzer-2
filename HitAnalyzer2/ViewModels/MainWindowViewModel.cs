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
        private readonly RecordingService _recording;
        private readonly DetectionSettings _detectionSettings;
        internal volatile bool IsTargetPreviewActive = true;
        internal volatile bool IsDarkTheme;

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
            _recording = recording;
            ShootingSettings = new ShootingSettingsViewModel(settings, shootingSession, recording);
            TargetSettings.SetShootingSettings(ShootingSettings);
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
            var detection = DetectionSettings.GetConfiguration(current.Detection);
            var weapon = WeaponSettings.GetSelectedProfile();
            var configuration = current with
            {
                Theme = ShootingSettings.SelectedTheme,
                Camera = camera,
                Target = TargetSettings.GetConfiguration(),
                Detection = detection,
                SelectedWeapon = weapon ?? current.SelectedWeapon,
                LegacyWeapon = weapon is null && current.SelectedWeapon is null
                    ? WeaponSettings.GetConfiguration(current.LegacyWeapon ?? new WeaponSettings())
                    : null
            };

            try
            {
                _settings.SaveConfiguration(configuration, weapon);
                if (weapon is not null && WeaponSettings.SelectedWeapon is { } selected)
                    selected.Profile = weapon.Profile;
                ConfigurationMessage = "Settings saved.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                ConfigurationMessage = $"Could not save settings. {ex.Message}";
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
            bool showTargetPreview = IsTargetPreviewActive;
            var (lastHit, aimPath, followPath, lastTargetHit, caliberDiameterMm) =
                _shootingSession.ProcessFrame(pixels, capturedAt, detectedFps, cancellationToken);
            if (showTargetPreview)
            {
                int size = Math.Min(pixels.Rows, pixels.Cols);
                using var target = new Mat(size, size, pixels.Type());
                var recordedHits = ShootingSettings.ShowAllHits ? _recording.GetNormalizedHitRecordList() : null;
                TargetRenderer.DrawTargetPreview(target, lastTargetHit, CameraSettings.Calibration.Settings,
                    caliberDiameterMm, recordedHits, IsDarkTheme);
                await CameraPreview.ShowFrameAsync(target, null, cancellationToken);
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
                AimTraceRenderer.DrawHitAndTraceIntoFrame(pixels, hit, aimPath, followPath,
                    caliberDiameterMm / CameraSettings.Calibration.Settings.MmToPixel);

            await CameraPreview.ShowFrameAsync(pixels, measuredFps, cancellationToken);
        }
    }
}
