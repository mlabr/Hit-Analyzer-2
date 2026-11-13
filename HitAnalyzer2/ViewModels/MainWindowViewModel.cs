using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Video;
using HitAnalyzer2.Services.Detection;
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

        public CameraSettingsViewModel CameraSettings { get; }
        public CameraPreviewViewModel CameraPreview { get; } = new();
        public DetectionSettingsViewModel DetectionSettings { get; }
        public ShootingSettingsViewModel ShootingSettings { get; }
        public WeaponSettingsViewModel WeaponSettings { get; }

        internal MainWindowViewModel(SettingsService settings, DetectionSettingsViewModel detectionSettings,
            WeaponSettingsViewModel weaponSettings, ShootingSessionService shootingSession,
            CameraCalibrationService cameraCalibration)
        {
            DetectionSettings = detectionSettings;
            _detectionSettings = settings.Current.Detection;
            WeaponSettings = weaponSettings;
            _shootingSession = shootingSession;
            ShootingSettings = new ShootingSettingsViewModel(shootingSession);
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
            using var output = new Mat();
            var pixels = PreviewFrameProcessor.Process(frame, output, DetectionSettings.BrightnessPercent,
                DetectionSettings.ContrastPercent, DetectionSettings.SaturationPercent);
            var (lastHit, currentAim, aimPath, followPath) =
                _shootingSession.ProcessFrame(pixels, capturedAt, detectedFps, cancellationToken);
            int? flareSensitivity = DetectionSettings.ShowFlares ? DetectionSettings.DetectionSensitivity : null;
            bool showAllowedArea = DetectionSettings.ShowAllowedArea;
            bool showAimPoint = DetectionSettings.ShowAimPointPreview && currentAim.HasValue;

            if (ReferenceEquals(pixels, frame) &&
                (lastHit.HasValue || flareSensitivity.HasValue || showAllowedArea || showAimPoint))
            {
                frame.CopyTo(output);
                pixels = output;
            }

            if (flareSensitivity is { } sensitivity)
                FlareDetectionOverlay.Draw(pixels, sensitivity);

            if (showAllowedArea)
                AllowedAreaRenderer.Draw(pixels, _detectionSettings);

            if (lastHit is { } hit)
                AimRenderer.Draw(pixels, hit, aimPath, followPath);

            if (showAimPoint && currentAim is { } aim)
            {
                var cameraPoint = new CalibrationPoint(aim.X / pixels.Width, aim.Y / pixels.Height);
                var targetPoint = CameraSettings.Calibration.MapHit(cameraPoint.X, cameraPoint.Y);
                AimRenderer.DrawAimPointPreview(pixels, targetPoint ?? cameraPoint);
            }

            await CameraPreview.ShowFrameAsync(pixels, fps, cancellationToken);
        }
    }
}
