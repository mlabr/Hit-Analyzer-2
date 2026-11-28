using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Shooting;
using OpenCvSharp;
using System.Threading;
using System.Threading.Tasks;
using Point = Avalonia.Point;

namespace HitAnalyzer2.ViewModels
{
    public partial class CameraPreviewViewModel : ViewModelBase
    {
        [ObservableProperty]
        private Bitmap? webcamImage;

        [ObservableProperty]
        private string? statusMessage;

        [ObservableProperty]
        private string? fpsText;

        [ObservableProperty]
        private double? weaponPreviewScore;
        [ObservableProperty]
        private double leftGuidanceLevel;
        [ObservableProperty]
        private double rightGuidanceLevel;
        [ObservableProperty]
        private Point? previewAim;

        [ObservableProperty]
        private Point? audioAim;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDetectionReady), nameof(IsDetectionTracking))]
        private HitDetectionReadiness detectionReadiness;

        public bool IsDetectionReady => DetectionReadiness == HitDetectionReadiness.Ready;
        public bool IsDetectionTracking => DetectionReadiness == HitDetectionReadiness.Tracking;

        internal Position? WeaponPreviewHit { get; private set; }

        internal async Task ShowFrameAsync(Mat pixels, string? measuredFps, CancellationToken cancellationToken,
            double? previewScore = null, Position? weaponPreviewHit = null,
            (double Left, double Right) guidance = default, HitDetectionReadiness readiness = default,
            Position? previewAim = null, Position? audioAim = null)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var bitmap = CreateBitmap(pixels);

            bool published = false;
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    var previous = WebcamImage;
                    published = true;
                    WeaponPreviewScore = previewScore;
                    WeaponPreviewHit = weaponPreviewHit;
                    LeftGuidanceLevel = guidance.Left;
                    RightGuidanceLevel = guidance.Right;
                    PreviewAim = previewAim is { } aim ? new Point(aim.X, aim.Y) : null;
                    AudioAim = audioAim is { } targetAim ? new Point(targetAim.X, targetAim.Y) : null;
                    DetectionReadiness = readiness;
                    WebcamImage = bitmap;
                    previous?.Dispose();
                    FpsText = measuredFps is null ? null : $"FPS {measuredFps}";
                    StatusMessage = null;
                }, DispatcherPriority.Background);
            }
            finally
            {
                if (!published)
                    bitmap.Dispose();
            }
        }

        internal static Bitmap CreateBitmap(Mat pixels) =>
            new WriteableBitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pixels.Data,
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), checked((int)pixels.Step()));

        internal void ClearPreview()
        {
            var previous = WebcamImage;
            WebcamImage = null;
            previous?.Dispose();
            FpsText = null;
            WeaponPreviewScore = null;
            WeaponPreviewHit = null;
            LeftGuidanceLevel = 0;
            RightGuidanceLevel = 0;
            PreviewAim = null;
            AudioAim = null;
            DetectionReadiness = HitDetectionReadiness.Waiting;
        }
    }
}
