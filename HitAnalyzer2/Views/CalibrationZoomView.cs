using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HitAnalyzer2.ViewModels;

namespace HitAnalyzer2.Views
{
    public sealed class CalibrationZoomView : Control
    {
        private Bitmap? _source;
        private Point _position;
        private double _zoom = 2;
        private readonly CameraCalibrationOverlay _calibrationOverlay = new();

        internal void Update(Bitmap? source, Point position, double zoom, CameraSettingsViewModel? settings)
        {
            _source = source;
            _position = position;
            _zoom = zoom;
            _calibrationOverlay.StrokeThickness = 1 / zoom;
            _calibrationOverlay.Update(settings, source is null ? default : new Rect(source.Size));
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            context.FillRectangle(Brushes.Black, bounds);
            if (_source is not { } source || bounds.Width <= 0 || bounds.Height <= 0)
                return;

            using var clip = context.PushClip(bounds);
            var requested = new Rect(_position.X - bounds.Width / (2 * _zoom),
                _position.Y - bounds.Height / (2 * _zoom), bounds.Width / _zoom, bounds.Height / _zoom);
            var cropped = requested.Intersect(new Rect(source.Size));
            if (cropped.Width > 0 && cropped.Height > 0)
            {
                var destination = new Rect((cropped.X - requested.X) * _zoom,
                    (cropped.Y - requested.Y) * _zoom, cropped.Width * _zoom, cropped.Height * _zoom);
                context.DrawImage(source, cropped, destination);
            }

            using (context.PushTransform(new Matrix(_zoom, 0, 0, _zoom,
                -requested.X * _zoom, -requested.Y * _zoom)))
                _calibrationOverlay.Render(context);

        }
    }
}
