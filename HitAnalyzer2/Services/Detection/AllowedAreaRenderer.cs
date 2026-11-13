using OpenCvSharp;
using System;

namespace HitAnalyzer2.Services.Detection
{
    internal static class AllowedAreaRenderer
    {
        private const int DashLength = 8;
        private const int GapLength = 5;
        private static readonly Scalar BoundaryColor = new(0, 255, 255, 255);  //new (0, 165, 255, 255);


        internal static void Draw(Mat preview, DetectionSettings settings,
            bool showForbiddenHatching = true, int hatchSpacing = 16)
        {
            var bounds = AllowedArea.GetBounds(preview, settings);
            if (showForbiddenHatching)
                DrawForbiddenHatching(preview, bounds, hatchSpacing);

            if (bounds.Width == 0 || bounds.Height == 0)
                return;

            if (bounds.Top > 0)
            {
                int right = Math.Min(bounds.Right, preview.Width - 1);
                DrawDashedLine(preview, bounds.Top, bounds.Left, right, horizontal: true);
                DrawDashedLine(preview, bounds.Bottom, bounds.Left, right, horizontal: true);
            }

            if (bounds.Left > 0)
            {
                int bottom = Math.Min(bounds.Bottom, preview.Height - 1);
                DrawDashedLine(preview, bounds.Left, bounds.Top, bottom, horizontal: false);
                DrawDashedLine(preview, bounds.Right, bounds.Top, bottom, horizontal: false);
            }
        }

        private static void DrawForbiddenHatching(Mat preview, Rect allowed, int spacing)
        {
            // At 45 degrees the distance between x + y intercepts is sqrt(2) times the perpendicular spacing.
            long step = (long)Math.Round(Math.Max(1, spacing) * Math.Sqrt(2));
            if (allowed.Width == 0 || allowed.Height == 0)
            {
                DrawHatching(preview, new Rect(0, 0, preview.Width, preview.Height), step);
                return;
            }

            DrawHatching(preview, new Rect(0, 0, preview.Width, allowed.Top), step);
            DrawHatching(preview, new Rect(0, allowed.Bottom, preview.Width, preview.Height - allowed.Bottom), step);
            DrawHatching(preview, new Rect(0, allowed.Top, allowed.Left, allowed.Height), step);
            DrawHatching(preview, new Rect(allowed.Right, allowed.Top, preview.Width - allowed.Right, allowed.Height), step);
        }

        private static void DrawHatching(Mat preview, Rect area, long step)
        {
            if (area.Width == 0 || area.Height == 0)
                return;

            long first = (long)area.Left + area.Top;
            long last = (long)area.Right + area.Bottom - 2;
            // Keep the same phase across all four strips so the diagonals align at the corners.
            for (long diagonal = (first + step - 1) / step * step; diagonal <= last; diagonal += step)
            {
                int left = (int)Math.Max(area.Left, diagonal - (area.Bottom - 1));
                int right = (int)Math.Min(area.Right - 1, diagonal - area.Top);
                Cv2.Line(preview, new Point(left, (int)(diagonal - left)),
                    new Point(right, (int)(diagonal - right)), BoundaryColor);
            }
        }

        private static void DrawDashedLine(Mat preview, int fixedCoordinate, int start, int end, bool horizontal)
        {
            for (int dashStart = start; dashStart <= end; dashStart += DashLength + GapLength)
            {
                int dashEnd = Math.Min(dashStart + DashLength - 1, end);
                var from = horizontal ? new Point(dashStart, fixedCoordinate) : new Point(fixedCoordinate, dashStart);
                var to = horizontal ? new Point(dashEnd, fixedCoordinate) : new Point(fixedCoordinate, dashEnd);
                Cv2.Line(preview, from, to, BoundaryColor);
            }
        }
    }
}
