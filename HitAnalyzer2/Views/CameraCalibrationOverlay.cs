using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HitAnalyzer2.Services.Target;
using HitAnalyzer2.ViewModels;
using System;
using System.Linq;

namespace HitAnalyzer2.Views
{
    public sealed class CameraCalibrationOverlay : Control
    {
        private const double HandleRadius = 6;
        private const double HitRadius = 12;
        private Rect _imageBounds;
        private CameraSettingsViewModel? _settings;
        private IPointer? _dragPointer;
        private int _dragCorner;
        private Vector _dragOffset;
        private CalibrationCorners? _translationStart;
        private Point _translationPointerStart;

        internal void Update(CameraSettingsViewModel? settings, Rect imageBounds)
        {
            bool visible = settings?.SelectedCalibrationMode.Mode == CameraCalibrationMode.HitOnly &&
                imageBounds.Width > 0 && imageBounds.Height > 0;
            if ((!visible || settings != _settings || imageBounds != _imageBounds) && _dragPointer is { } pointer)
                pointer.Capture(null);

            _settings = settings;
            _imageBounds = imageBounds;
            if (visible && settings!.Calibration.Settings.Corners is null)
                settings.SetCalibrationSquare(imageBounds.Width, imageBounds.Height);
            IsVisible = visible;
            InvalidateVisual();
        }

        internal void ResetToSquare()
        {
            if (!IsVisible || _settings is not { } settings)
                return;

            _dragPointer?.Capture(null);
            settings.SetCalibrationSquare(_imageBounds.Width, _imageBounds.Height);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (_settings is not { } settings)
                return;

            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            var points = GetPreviewCorners(settings);
            var brush = settings.Calibration.IsValid ? Brushes.Yellow : Brushes.Cyan;
            var pen = new Pen(brush, 2);
            for (int i = 0; i < points.Length; i++)
                context.DrawLine(pen, points[i], points[(i + 1) % points.Length]);

            var outline = settings.Calibration.CameraTargetOutline;
            if (!outline.IsEmpty)
            {
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(ToPreview(outline[0]), isFilled: false);
                    foreach (var point in outline[1..])
                        path.LineTo(ToPreview(point));
                    path.EndFigure(isClosed: true);
                }
                context.DrawGeometry(null, pen, geometry);
            }

            foreach (var point in points)
                context.DrawEllipse(brush, null, point, HandleRadius, HandleRadius);

            if (settings.Calibration.CameraTargetCenter is { } center)
                context.DrawEllipse(brush, null, ToPreview(center), HandleRadius, HandleRadius);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_settings is not { } settings || _dragPointer is not null ||
                e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
                return;

            var pointerPosition = e.GetPosition(this);
            var points = GetPreviewCorners(settings);
            double closestSquared = HitRadius * HitRadius;
            int corner = -1;
            for (int i = 0; i < points.Length; i++)
            {
                double distanceSquared = DistanceSquared(points[i], pointerPosition);
                if (distanceSquared < closestSquared)
                {
                    closestSquared = distanceSquared;
                    corner = i;
                }
            }

            if (settings.Calibration.CameraTargetCenter is { } center &&
                settings.Calibration.Settings.Corners is { } corners &&
                DistanceSquared(ToPreview(center), pointerPosition) < closestSquared)
            {
                _translationStart = corners;
                _translationPointerStart = pointerPosition;
            }
            else if (corner >= 0)
            {
                _dragCorner = corner;
                _dragOffset = points[corner] - pointerPosition;
            }
            else
                return;

            _dragPointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_dragPointer != e.Pointer || _settings is not { } settings)
                return;

            var pointer = e.GetPosition(this);
            if (_translationStart is { } start)
            {
                CalibrationPoint[] corners = [start.TopLeft, start.TopRight, start.BottomRight, start.BottomLeft];
                double dx = Math.Clamp((pointer.X - _translationPointerStart.X) / _imageBounds.Width,
                    -corners.Min(point => point.X), 1 - corners.Max(point => point.X));
                double dy = Math.Clamp((pointer.Y - _translationPointerStart.Y) / _imageBounds.Height,
                    -corners.Min(point => point.Y), 1 - corners.Max(point => point.Y));
                CalibrationPoint Translate(CalibrationPoint point) => new(point.X + dx, point.Y + dy);
                settings.SetCalibrationCorners(new(Translate(start.TopLeft), Translate(start.TopRight),
                    Translate(start.BottomRight), Translate(start.BottomLeft)));
            }
            else
            {
                var position = pointer + _dragOffset;
                settings.MoveCalibrationCorner(_dragCorner, new CalibrationPoint(
                    Math.Clamp((position.X - _imageBounds.X) / _imageBounds.Width, 0, 1),
                    Math.Clamp((position.Y - _imageBounds.Y) / _imageBounds.Height, 0, 1)));
            }
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_dragPointer == e.Pointer && e.InitialPressMouseButton == MouseButton.Left)
            {
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            if (_dragPointer == e.Pointer)
            {
                _dragPointer = null;
                _translationStart = null;
            }
        }

        private static double DistanceSquared(Point a, Point b)
        {
            var delta = a - b;
            return delta.X * delta.X + delta.Y * delta.Y;
        }
        private Point[] GetPreviewCorners(CameraSettingsViewModel settings)
        {
            if (settings.Calibration.Settings.Corners is not { } corners)
                return [];
            return [ToPreview(corners.TopLeft), ToPreview(corners.TopRight),
                ToPreview(corners.BottomRight), ToPreview(corners.BottomLeft)];
        }

        private Point ToPreview(CalibrationPoint point) => new(
            _imageBounds.X + point.X * _imageBounds.Width,
            _imageBounds.Y + point.Y * _imageBounds.Height);
    }
}
