using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Target;
using OpenCvSharp;
using System;
using System.Globalization;
using System.Linq;

namespace HitAnalyzer2.Services.Rendering
{
    internal static class TargetRenderer
    {
        private const double PreviewMargin = TargetPreviewModel.PreviewMargin;
        private static readonly Scalar AimMarkerOutline = new(20, 20, 20, 255);

        internal static void DrawTargetPreview(Mat preview, Position? lastTargetHit, TargetModel settings)
        {
            preview.SetTo(new Scalar(211, 211, 211, 255));
            int size = Math.Min(preview.Width, preview.Height);
            if (size < 2)
                return;

            double diameterPixels = TargetPreviewModel.GetPreviewDiameterPixels(size);
            using var target = new Mat(preview, new Rect((preview.Width - size) / 2, (preview.Height - size) / 2, size, size));
            var guide = new Scalar(0, 0, 0, 255);
            Cv2.Rectangle(target, default(Point), new Point(size - 1, size - 1), guide);
            var center = ToTargetPixel(TargetCalibrationService.TargetCenter);
            DrawTargetRings(target, center, diameterPixels, settings, guide);

            if (lastTargetHit is { } hit && IsInsidePreview(hit))
            {
                int markerSize = Math.Max(1, (int)Math.Round(target.Height * AimTracerRenderer.MarkerSizeRatio));
                var marker = ToTargetPixel(hit);
                AimTracerRenderer.DrawAimPosition(target, marker.X, marker.Y, markerSize,
                    AimTracerRenderer.AimPositionMarkerColor, AimMarkerOutline);
            }

            Point ToTargetPixel(Position point) =>
                new(Math.Round((point.X + PreviewMargin) * diameterPixels),
                    Math.Round((point.Y + PreviewMargin) * diameterPixels));
        }

        private static void DrawTargetRings(Mat preview, Point center, double diameterPixels,
            TargetModel settings, Scalar outline)
        {
            double outerRadius = TargetCalibrationService.TargetRadius * diameterPixels;
            int outerRadiusPixels = (int)Math.Round(outerRadius);
            Cv2.Circle(preview, center, outerRadiusPixels, new Scalar(255, 255, 255, 255), -1);

            if (settings.MaxDiameter is not { } maxDiameter)
            {
                Cv2.Circle(preview, center, outerRadiusPixels, outline);
                Cv2.Circle(preview, center, 2, outline, -1);
                return;
            }

            var markedRing = settings.Rings.FirstOrDefault(ring => ring.PointValue == settings.MarkedRingPoints);
            if (markedRing is not null)
                Cv2.Circle(preview, center, RingRadius(markedRing), new Scalar(64, 64, 64, 255), -1);

            var innerOutline = new Scalar(192, 192, 192, 255);
            foreach (var ring in settings.Rings)
            {
                var color = markedRing is not null && ring.Diameter < markedRing.Diameter ? innerOutline : outline;
                Cv2.Circle(preview, center, RingRadius(ring), color, lineType: LineTypes.AntiAlias);
            }

            double innerRadius = 0;
            foreach (var ring in settings.Rings.OrderBy(ring => ring.Diameter))
            {
                double radius = ring.Diameter / maxDiameter * outerRadius;
                var color = markedRing is not null && ring.Diameter <= markedRing.Diameter
                    ? innerOutline : new Scalar(64, 64, 64, 255);
                DrawRingValue(preview, center, innerRadius, radius, ring.PointValue, color);
                innerRadius = radius;
            }

            int RingRadius(TargetRingModel ring) => (int)Math.Round(ring.Diameter / maxDiameter * outerRadius);
        }

        private static void DrawRingValue(Mat preview, Point center, double innerRadius, double outerRadius,
            double pointValue, Scalar color)
        {
            if (pointValue == 10)
                return;

            const HersheyFonts font = HersheyFonts.HersheySimplex;
            const int thickness = 1;
            string text = pointValue.ToString("G", CultureInfo.InvariantCulture);
            double radius = (innerRadius + outerRadius) / 2;
            double bandWidth = outerRadius - innerRadius;
            var fullSize = Cv2.GetTextSize(text, font, 1, thickness, out _);
            double scale = Math.Min(0.55, (Math.Min(bandWidth, radius) - 2) / Math.Max(fullSize.Width, fullSize.Height));
            if (scale < 0.25)
                return;

            var size = Cv2.GetTextSize(text, font, scale, thickness, out _);
            Draw(radius, 0);
            Draw(-radius, 0);
            Draw(0, radius);
            Draw(0, -radius);

            void Draw(double x, double y)
            {
                var origin = new Point(Math.Round(center.X + x - size.Width / 2.0),
                    Math.Round(center.Y + y + size.Height / 2.0));
                Cv2.PutText(preview, text, origin, font, scale, color, thickness, LineTypes.AntiAlias);
            }
        }

        private static bool IsInsidePreview(Position point)
        {
            const double min = -PreviewMargin - 0.000001;
            const double max = 1 + PreviewMargin + 0.000001;
            return point is { X: >= min and <= max, Y: >= min and <= max };
        }
    }
}
