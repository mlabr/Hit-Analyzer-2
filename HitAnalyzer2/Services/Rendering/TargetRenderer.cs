using HitAnalyzer2.Models;
using HitAnalyzer2.Models.Shooting;
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
            Position? originalTargetHit = null, HitGroupCircle? cep50 = null,
            HitGroupCircle? cep80 = null, HitGroupCircle? smallestCircle = null, int directionCount = 0,
            IReadOnlyList<LaserPosition>? aimPath = null, IReadOnlyList<LaserPosition>? followPath = null,
            Func<double, double, Position?>? mapFollowPoint = null, Position? liveLaser = null)
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
            if (settings.IsMonoscope)
                DrawMonoscope(preview, ToTargetPixel(new Position(0, 0)), ToTargetPixel(new Position(1, 1)));
            else
                DrawTargetRings(preview, center, diameterPixels, settings, palette);

            if (directionCount > 0)
                DrawDirectionSectors(preview, center, diameterPixels / 2, directionCount, palette);

            if (aimPath is not null)
                AimTraceRenderer.DrawAimPath(preview, aimPath, AimTraceRenderer.AimPathBeforeColor,
                    default, oldestIsBrightest: false, project: (x, y) => ToTargetPixel(new Position(x, y)));

            if (followPath is not null && mapFollowPoint is not null)
                AimTraceRenderer.DrawAimPath(preview, followPath, AimTraceRenderer.AimPathAfterColor,
                    default, oldestIsBrightest: true,
                    project: (x, y) => mapFollowPoint(x, y) is { } point ? ToTargetPixel(point) : null);

            if (originalTargetHit is { } original && IsInsidePreview(original))
            {
                var marker = ToTargetPixel(original);
                AimTraceRenderer.DrawLaserPosition(preview, marker.X, marker.Y, markerSize,
                    AimTraceRenderer.LaserPositionMarkerColor);
            }

            if (recordedHits is not null)
            {
                foreach (var record in recordedHits)
                    DrawHit(record.HitPosition);
            }
            else if (lastTargetHit is { } hit)
                DrawHit(hit);

            if (liveLaser is { } laser && IsInsidePreview(laser))
            {
                var point = ToTargetPixel(laser);
                Cv2.Circle(preview, point, 8, palette.HitOutline, 4, LineTypes.AntiAlias);
                Cv2.Circle(preview, point, 8, AimTraceRenderer.AimPathAfterColor, 2, LineTypes.AntiAlias);
            }

            Scalar meanColor = isDarkTheme ? new(209, 213, 169, 255) : new(101, 93, 36, 255);
            Scalar cep50Color = isDarkTheme ? new(242, 183, 212, 255) : new(159, 73, 206, 255);
            Scalar smallestColor = isDarkTheme ? new(125, 189, 249, 255) : new(17, 78, 163, 255);
            if (smallestCircle is { Radius: > 0 } smallest)
                Cv2.Circle(preview, ToTargetPixel(smallest.Center), RadiusPixels(smallest), smallestColor, 2, LineTypes.AntiAlias);
            if (cep50 is { Radius: > 0 } inner)
                Cv2.Circle(preview, ToTargetPixel(inner.Center), RadiusPixels(inner), cep50Color, 2, LineTypes.AntiAlias);
            if (cep80 is { Radius: > 0 } group)
            {
                int radius = RadiusPixels(group);
                int arcStep = Math.Clamp((int)Math.Round(360 / (Math.Tau * radius) * 12), 6, 45);
                for (int angle = 0; angle < 360; angle += arcStep)
                    Cv2.Ellipse(preview, ToTargetPixel(group.Center), new Size(radius, radius), 0,
                        angle, Math.Min(360, angle + arcStep * 0.6), meanColor, 2, LineTypes.AntiAlias);
            }
            if (cep80?.Center is { } mean && IsInsidePreview(mean))
            {
                var marker = ToTargetPixel(mean);
                Cv2.DrawMarker(preview, marker, palette.HitOutline, MarkerTypes.Cross, 12, 4, LineTypes.AntiAlias);
                Cv2.DrawMarker(preview, marker, meanColor, MarkerTypes.Cross, 12, 2, LineTypes.AntiAlias);
            }

            int RadiusPixels(HitGroupCircle circle) => Math.Max(1, (int)Math.Round(circle.Radius * diameterPixels));

            void DrawHit(Position hit)
            {
                if (!IsInsidePreview(hit))
                    return;

                var marker = ToTargetPixel(hit);
                double? projectileDiameterPixels = caliberDiameterMm / settings.MaxDiameterMm * diameterPixels;
                AimTraceRenderer.DrawAimPosition(preview, marker.X, marker.Y, markerSize,
                    palette.Hit, projectileDiameterPixels, palette.HitOutline);
            }

            Point ToTargetPixel(Position point)
            {
                var previewPoint = GetPreviewPosition(point, settings);
                return new Point(Math.Round(previewPoint.X * (size - 1)), Math.Round(previewPoint.Y * (size - 1)));
            }
        }

        internal static Position GetPreviewPosition(Position point, TargetModel target)
        {
            double width = 1 / TargetPreviewModel.PreviewSpan;
            double height = width;
            if (target.IsMonoscope && target.Width > 0 && target.Height > 0)
            {
                width *= (double)target.Width / Math.Max(target.Width, target.Height);
                height *= (double)target.Height / Math.Max(target.Width, target.Height);
            }

            return new Position(0.5 + (point.X - 0.5) * width, 0.5 + (point.Y - 0.5) * height);
        }

        private static void DrawDirectionSectors(Mat preview, Point center, double radius, int count,
            PreviewPalette palette)
        {
            const HersheyFonts font = HersheyFonts.HersheySimplex;
            double labelScale = Math.Max(0.4, radius / 155);
            double sectorAngle = Math.Tau / count;
            for (int i = 0; i < count; i++)
            {
                double boundaryAngle = (i + 0.5) * sectorAngle;
                // Extend past the square so OpenCV clips the ray at the preview edge.
                var edge = At(boundaryAngle, Math.Min(preview.Width, preview.Height));
                Cv2.Line(preview, center, edge, palette.Background, 3, LineTypes.AntiAlias);
                Cv2.Line(preview, center, edge, palette.Text, 1, LineTypes.AntiAlias);

                string label = (i == 0 ? count : i).ToString(CultureInfo.InvariantCulture);
                var labelCenter = At(i * sectorAngle, radius * 1.15);
                var textSize = Cv2.GetTextSize(label, font, labelScale, 2, out _);
                var origin = new Point(labelCenter.X - textSize.Width / 2, labelCenter.Y + textSize.Height / 2);
                Cv2.PutText(preview, label, origin, font, labelScale, palette.Text, 2, LineTypes.AntiAlias);
            }

            Point At(double angle, double distance) =>
                new(center.X + Math.Round(Math.Sin(angle) * distance),
                    center.Y - Math.Round(Math.Cos(angle) * distance));
        }

        private static void DrawMonoscope(Mat preview, Point topLeft, Point bottomRight)
        {
            var black = new Scalar(16, 16, 16, 255);
            var white = new Scalar(244, 244, 244, 255);
            var grid = new Scalar(190, 190, 190, 255);
            int width = bottomRight.X - topLeft.X;
            int height = bottomRight.Y - topLeft.Y;
            int shortSide = Math.Min(width, height);
            Cv2.Rectangle(preview, topLeft, bottomRight, white, -1);
            Cv2.Rectangle(preview, topLeft, bottomRight, black);
            if (shortSide < 16)
                return;

            Point At(double x, double y) =>
                new(topLeft.X + Math.Round(x * width), topLeft.Y + Math.Round(y * height));

            for (int i = 1; i < 8; i++)
            {
                double step = i / 8.0;
                Cv2.Line(preview, At(step, 0.08), At(step, 0.80), grid);
                Cv2.Line(preview, At(0.08, step * 0.80), At(0.92, step * 0.80), grid);
            }

            var center = At(0.5, 0.5);
            int radius = (int)Math.Round(shortSide * 0.28);
            Cv2.Circle(preview, center, radius, black, 1, LineTypes.AntiAlias);
            Cv2.Line(preview, At(0.08, 0.5), At(0.92, 0.5), black, 1, LineTypes.AntiAlias);
            Cv2.Line(preview, At(0.5, 0.08), At(0.5, 0.80), black, 1, LineTypes.AntiAlias);

            const int levels = 7;
            for (int i = 0; i < levels; i++)
            {
                double shade = 255.0 * i / (levels - 1);
                Cv2.Rectangle(preview, At(0.18 + 0.64 * i / levels, 0.84),
                    At(0.18 + 0.64 * (i + 1) / levels, 0.94), new Scalar(shade, shade, shade, 255), -1);
            }
            Cv2.Rectangle(preview, At(0.18, 0.84), At(0.82, 0.94), black);

            DrawCorner(0.04, 0.05, 1, 1);
            DrawCorner(0.96, 0.05, -1, 1);
            DrawCorner(0.04, 0.95, 1, -1);
            DrawCorner(0.96, 0.95, -1, -1);

            void DrawCorner(double x, double y, int directionX, int directionY)
            {
                int arm = Math.Max(2, shortSide / 12);
                int thickness = Math.Max(1, shortSide / 75);
                var corner = At(x, y);
                Cv2.Line(preview, corner, new Point(corner.X + directionX * arm, corner.Y), black, thickness);
                Cv2.Line(preview, corner, new Point(corner.X, corner.Y + directionY * arm), black, thickness);
            }
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
