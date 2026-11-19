using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Target;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HitAnalyzer2.Services.Rendering
{
    internal static class TargetRenderer
    {
        private const double PreviewMargin = TargetPreviewModel.PreviewMargin;
        // OpenCV stores colors in BGRA order.
        private sealed record PreviewPalette(Scalar Background, Scalar Paper, Scalar Outline,
            Scalar MarkedArea, Scalar InnerOutline, Scalar Text, Scalar Hit, Scalar HitOutline);

        private static readonly PreviewPalette LightPalette = new(
            new(211, 211, 211, 255), new(255, 255, 255, 255), new(0, 0, 0, 255),
            new(64, 64, 64, 255), new(192, 192, 192, 255), new(64, 64, 64, 255),
            AimTraceRenderer.AimPositionMarkerColor, new(20, 20, 20, 255));

        private static readonly PreviewPalette DarkPalette = new(
            new(40, 37, 24, 255), new(73, 68, 48, 255), new(170, 167, 145, 255),
            new(36, 33, 22, 255), new(170, 167, 145, 255), new(218, 219, 207, 255),
            new(187, 178, 230, 255), new(40, 37, 24, 255));

        internal static void DrawTargetPreview(Mat preview, Position? lastTargetHit, TargetModel settings,
            double caliberDiameterMm, IReadOnlyList<HitRecord>? recordedHits = null, bool isDarkTheme = false,
            Position? originalTargetHit = null, bool highlightLastRecordedHit = false)
        {
            var palette = isDarkTheme ? DarkPalette : LightPalette;
            preview.SetTo(palette.Background);
            int size = Math.Min(preview.Width, preview.Height);
            if (size < 2)
                return;

            double diameterPixels = TargetPreviewModel.GetPreviewDiameterPixels(size);
            int markerSize = Math.Max(1, (int)Math.Round(size * AimTraceRenderer.MarkerSizeRatio));
            var guide = palette.Outline;
            Cv2.Rectangle(preview, default(Point), new Point(size - 1, size - 1), guide);
            var center = ToTargetPixel(TargetModel.Center);
            DrawTargetRings(preview, center, diameterPixels, settings, palette);

            if (originalTargetHit is { } original && IsInsidePreview(original))
            {
                var marker = ToTargetPixel(original);
                AimTraceRenderer.DrawLaserPosition(preview, marker.X, marker.Y, markerSize,
                    AimTraceRenderer.LaserPositionMarkerColor);
            }

            if (recordedHits is not null)
            {
                for (int i = 0; i < recordedHits.Count; i++)
                    DrawHit(recordedHits[i].HitPosition, highlightLastRecordedHit && i == recordedHits.Count - 1);
            }
            else if (lastTargetHit is { } hit)
                DrawHit(hit);

            void DrawHit(Position hit, bool highlight = false)
            {
                if (!IsInsidePreview(hit))
                    return;

                var marker = ToTargetPixel(hit);
                double? projectileDiameterPixels = caliberDiameterMm / settings.MaxDiameterMm * diameterPixels;
                if (highlight)
                {
                    int radius = (int)Math.Ceiling(Math.Max(markerSize / 2.0,
                        projectileDiameterPixels.GetValueOrDefault() / 2.0)) + 4;
                    Cv2.Circle(preview, marker, radius, palette.HitOutline, 4, LineTypes.AntiAlias);
                    Cv2.Circle(preview, marker, radius, palette.Hit, 2, LineTypes.AntiAlias);
                }
                AimTraceRenderer.DrawAimPosition(preview, marker.X, marker.Y, markerSize,
                    palette.Hit, projectileDiameterPixels, palette.HitOutline);
            }

            Point ToTargetPixel(Position point) =>
                new(Math.Round((point.X + PreviewMargin) * diameterPixels),
                    Math.Round((point.Y + PreviewMargin) * diameterPixels));
        }

        private static void DrawTargetRings(Mat preview, Point center, double diameterPixels,
            TargetModel settings, PreviewPalette palette)
        {
            double outerRadius = TargetCalibrationService.TargetRadius * diameterPixels;
            int outerRadiusPixels = (int)Math.Round(outerRadius);
            Cv2.Circle(preview, center, outerRadiusPixels, palette.Paper, -1);

            if (settings.MaxDiameter is not { } maxDiameter)
            {
                Cv2.Circle(preview, center, outerRadiusPixels, palette.Outline);
                Cv2.Circle(preview, center, 2, palette.Outline, -1);
                return;
            }

            var markedRing = settings.Rings.FirstOrDefault(ring => ring.PointValue == settings.MarkedRingPoints);
            if (markedRing is not null)
                Cv2.Circle(preview, center, RingRadius(markedRing), palette.MarkedArea, -1);

            var innerOutline = palette.InnerOutline;
            foreach (var ring in settings.Rings)
            {
                var color = markedRing is not null && ring.Diameter < markedRing.Diameter ? innerOutline : palette.Outline;
                Cv2.Circle(preview, center, RingRadius(ring), color, lineType: LineTypes.AntiAlias);
            }

            double innerRadius = 0;
            foreach (var ring in settings.Rings.OrderBy(ring => ring.Diameter))
            {
                double radius = ring.Diameter / maxDiameter * outerRadius;
                var color = markedRing is not null && ring.Diameter <= markedRing.Diameter
                    ? innerOutline : palette.Text;
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
