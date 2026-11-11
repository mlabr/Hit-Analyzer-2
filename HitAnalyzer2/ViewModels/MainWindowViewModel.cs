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
        public CameraSettingsViewModel CameraSettings { get; }
        public CameraPreviewViewModel CameraPreview { get; } = new();
        public DetectionSettingsViewModel DetectionSettings { get; }
        public ShootingSettingsViewModel ShootingSettings { get; } = new();

        internal MainWindowViewModel(SettingsService settings, DetectionSettingsViewModel detectionSettings,
            Func<Mat, long, LaserPosition?> processFrame)
        {
            DetectionSettings = detectionSettings;
            var captureSession = new CameraCaptureSession(
                ShowCameraFrameAsync,
                CameraPreview.ClearPreview,
                message => CameraPreview.StatusMessage = message,
                processFrame);

            CameraSettings = new CameraSettingsViewModel(settings, captureSession);
        }

        internal Task RefreshCamerasAsync() => CameraSettings.RefreshCamerasAsync();

        internal Task StopCameraAsync() => CameraSettings.StopCameraAsync();

        private Task ShowCameraFrameAsync(Mat frame, LaserPosition? position, double? fps,
            CancellationToken cancellationToken) =>
            CameraPreview.ShowFrameAsync(frame, position, fps, CameraSettings.CameraBrightnessPercent,
                CameraSettings.CameraContrastPercent, CameraSettings.CameraSaturationPercent, cancellationToken);
    }
}
