using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System.Threading;
using System.Threading.Tasks;

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

        internal async Task ShowFrameAsync(Mat frame, LaserPosition? position, double? fps,
            int brightnessPercent, int contrastPercent, int saturationPercent, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            Bitmap bitmap;
            using (var output = new Mat())
            {
                var pixels = PreviewFrameProcessor.Process(frame, output,
                    brightnessPercent, contrastPercent, saturationPercent);
                if (position is { } hit)
                {
                    if (ReferenceEquals(pixels, frame))
                    {
                        frame.CopyTo(output);
                        pixels = output;
                    }

                    HitRenderer.Draw(pixels, hit);
                }

                bitmap = new WriteableBitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pixels.Data,
                    new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), checked((int)pixels.Step()));
            }

            bool published = false;
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    var previous = WebcamImage;
                    published = true;
                    WebcamImage = bitmap;
                    previous?.Dispose();
                    FpsText = fps is { } value ? $"Náhled: {value:0.0} FPS" : null;
                    StatusMessage = null;
                }, DispatcherPriority.Background);
            }
            finally
            {
                if (!published)
                    bitmap.Dispose();
            }
        }

        internal void ClearPreview()
        {
            var previous = WebcamImage;
            WebcamImage = null;
            previous?.Dispose();
            FpsText = null;
        }
    }
}
