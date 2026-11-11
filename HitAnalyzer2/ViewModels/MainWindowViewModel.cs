using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HitAnalyzer2.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly Func<Mat, long, LaserPosition?> _detectHit;

        public CameraSettingsViewModel CameraSettings { get; }
        public CameraPreviewViewModel CameraPreview { get; } = new();
        public DetectionSettingsViewModel DetectionSettings { get; }
        public ShootingSettingsViewModel ShootingSettings { get; } = new();

        internal MainWindowViewModel(SettingsService settings, DetectionSettingsViewModel detectionSettings,
            Func<Mat, long, LaserPosition?> processFrame)
        {
            DetectionSettings = detectionSettings;
            _detectHit = processFrame;
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
            var position = _detectHit(pixels, capturedAt);
            int? flareSensitivity = DetectionSettings.ShowFlares ? DetectionSettings.DetectionSensitivity : null;

            if (ReferenceEquals(pixels, frame) && (position.HasValue || flareSensitivity.HasValue))
            {
                frame.CopyTo(output);
                pixels = output;
            }

            if (flareSensitivity is { } sensitivity)
                FlareDetectionOverlay.Draw(pixels, sensitivity);

            if (position is { } hit)
                HitRenderer.Draw(pixels, hit);

            await CameraPreview.ShowFrameAsync(pixels, fps, cancellationToken);
        }
    }
}
