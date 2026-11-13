using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Services.Rendering;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Target;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HitAnalyzer2.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly ShootingSessionService _shootingSession;
        private readonly DetectionSettings _detectionSettings;
        internal volatile bool IsShootingTabActive = true;

        public CameraSettingsViewModel CameraSettings { get; }
        public CameraPreviewViewModel CameraPreview { get; } = new();
        public DetectionSettingsViewModel DetectionSettings { get; }
        public ShootingSettingsViewModel ShootingSettings { get; }
        public WeaponSettingsViewModel WeaponSettings { get; }

        internal MainWindowViewModel(SettingsService settings, DetectionSettingsViewModel detectionSettings,
            WeaponSettingsViewModel weaponSettings, ShootingSessionService shootingSession,
            TargetCalibrationService cameraCalibration)
        {
            DetectionSettings = detectionSettings;
            _detectionSettings = settings.Current.Detection;
            WeaponSettings = weaponSettings;
            _shootingSession = shootingSession;
            ShootingSettings = new ShootingSettingsViewModel(settings, shootingSession);
            var captureSession = new CameraCaptureSession(
                ProcessCameraFrameAsync,
                () =>
                {
                    shootingSession.ClearAimPaths();
                    CameraPreview.ClearPreview();
                },
                message => CameraPreview.StatusMessage = message);

            CameraSettings = new CameraSettingsViewModel(settings, captureSession, cameraCalibration);
        }

        internal Task RefreshCamerasAsync() => CameraSettings.RefreshCamerasAsync();

        internal Task StopCameraAsync() => CameraSettings.StopCameraAsync();

        private async Task ProcessCameraFrameAsync(Mat frame, long capturedAt, double? fps, double detectedFps,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CameraSettings.Calibration.EnsureDefault(frame.Width, frame.Height);
            using var output = new Mat();
            var pixels = FramePreprocessor.ApplyImageAdjustments(frame, output, DetectionSettings.BrightnessPercent,
                DetectionSettings.ContrastPercent, DetectionSettings.SaturationPercent);
            bool showTargetPreview = IsShootingTabActive && ShootingSettings.ShowTargetPreview;
            var (lastHit, currentAim, aimPath, followPath, lastTargetHit) =
                _shootingSession.ProcessFrame(pixels, capturedAt, detectedFps, cancellationToken);
            if (showTargetPreview)
            {
                using var target = new Mat(pixels.Rows, pixels.Cols, pixels.Type());
                TargetRender.DrawTargetPreview(target, currentAim, lastHit, aimPath, followPath,
                    lastTargetHit, CameraSettings.Calibration);
                await CameraPreview.ShowFrameAsync(target, fps, cancellationToken);
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

            await CameraPreview.ShowFrameAsync(pixels, fps, cancellationToken);
        }
    }
}
