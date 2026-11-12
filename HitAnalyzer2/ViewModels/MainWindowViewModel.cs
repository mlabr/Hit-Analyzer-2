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

        public CameraSettingsViewModel CameraSettings { get; }
        public CameraPreviewViewModel CameraPreview { get; } = new();
        public DetectionSettingsViewModel DetectionSettings { get; }
        public ShootingSettingsViewModel ShootingSettings { get; }
        public WeaponSettingsViewModel WeaponSettings { get; }

        internal MainWindowViewModel(SettingsService settings, DetectionSettingsViewModel detectionSettings,
            WeaponSettingsViewModel weaponSettings, ShootingSessionService shootingSession)
        {
            DetectionSettings = detectionSettings;
            WeaponSettings = weaponSettings;
            _shootingSession = shootingSession;
            ShootingSettings = new ShootingSettingsViewModel(shootingSession);
            var captureSession = new CameraCaptureSession(
                ProcessCameraFrameAsync,
                CameraPreview.ClearPreview,
                message => CameraPreview.StatusMessage = message);

            CameraSettings = new CameraSettingsViewModel(settings, captureSession);
        }

        internal Task RefreshCamerasAsync() => CameraSettings.RefreshCamerasAsync();

        internal Task StopCameraAsync() => CameraSettings.StopCameraAsync();

        private async Task ProcessCameraFrameAsync(Mat frame, long capturedAt, double? fps,
            CancellationToken cancellationToken)
        {
            using var output = new Mat();
            var pixels = PreviewFrameProcessor.Process(frame, output, CameraSettings.CameraBrightnessPercent,
                CameraSettings.CameraContrastPercent, CameraSettings.CameraSaturationPercent);
            cancellationToken.ThrowIfCancellationRequested();
            var lastHit = _shootingSession.ProcessFrame(pixels, capturedAt);
            int? flareSensitivity = DetectionSettings.ShowFlares ? DetectionSettings.DetectionSensitivity : null;

            if (ReferenceEquals(pixels, frame) && (lastHit.HasValue || flareSensitivity.HasValue))
            {
                frame.CopyTo(output);
                pixels = output;
            }

            if (flareSensitivity is { } sensitivity)
                FlareDetectionOverlay.Draw(pixels, sensitivity);

            if (lastHit is { } hit)
                HitRenderer.Draw(pixels, hit);

            await CameraPreview.ShowFrameAsync(pixels, fps, cancellationToken);
        }
    }
}
