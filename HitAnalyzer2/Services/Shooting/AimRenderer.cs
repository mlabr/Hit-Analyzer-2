using HitAnalyzer2.Services.Detection;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace HitAnalyzer2.Services.Shooting
{
    internal static class AimRenderer
    {
        internal const double MarkerSizeRatio = 0.05;
        private const int MinimumAimPathBrightness = 64;
		internal static readonly Scalar AimPositionMarkerColor = new(0, 255, 255, 255);
		internal static readonly Scalar LaserPositionMarkerColor = new(255, 0, 0, 255);
        internal static readonly Scalar AimPathBeforeColor = new(0, 255, 255, 255);
        internal static readonly Scalar AimPathAfterColor = new(255, 255, 0, 255);

        internal static void Draw(Mat preview, AimPosition position, IReadOnlyList<LaserPosition> aimPath,
            IReadOnlyList<LaserPosition> followPath)
        {
            var laser = position.LaserPosition;
            var offset = (X: position.X - laser.X, Y: position.Y - laser.Y);
            DrawAimPath(preview, aimPath, AimPathBeforeColor, offset, oldestIsBrightest: false);
            DrawAimPath(preview, followPath, AimPathAfterColor, offset, oldestIsBrightest: true);

            int size = Math.Max(1, (int)Math.Round(preview.Height * MarkerSizeRatio));
            if (offset.X != 0 || offset.Y != 0)
				DrawLaserPosition(preview, laser.X, laser.Y, size, LaserPositionMarkerColor);

			DrawAimPosition(preview, position.X, position.Y, size, AimPositionMarkerColor);
        }

        internal static void DrawAimPath(Mat preview, IReadOnlyList<LaserPosition> aimPath, Scalar baseColor,
            (double X, double Y) offset, bool oldestIsBrightest, Func<double, double, Point?>? project = null)
        {
            if (aimPath.Count < 2)
                return;

            var firstSegmentTime = aimPath[1].Time;
            double duration = (aimPath[^1].Time - firstSegmentTime).TotalMilliseconds;
            for (int i = 1; i < aimPath.Count; i++)
            {
                var previous = aimPath[i - 1];
                var current = aimPath[i];
                double intensity = 1;
                if (duration > 0)
                {
                    double progress = (current.Time - firstSegmentTime).TotalMilliseconds / duration;
                    intensity = oldestIsBrightest ? 1 - progress : progress;
                }
                double brightness = Math.Round(MinimumAimPathBrightness + (255 - MinimumAimPathBrightness) * intensity) / 255;
                var color = new Scalar(baseColor.Val0 * brightness, baseColor.Val1 * brightness,
                    baseColor.Val2 * brightness, baseColor.Val3);
                var start = GetPoint(previous);
                var end = GetPoint(current);
                if (start is { } from && end is { } to)
                    Cv2.Line(preview, from, to, color, 1);
            }

            Point? GetPoint(LaserPosition position)
            {
                double x = position.X + offset.X;
                double y = position.Y + offset.Y;
                return project is null ? new Point(Math.Round(x), Math.Round(y)) : project(x, y);
            }
        }

        internal static void DrawAimPosition(Mat preview, double positionX, double positionY, int size, Scalar color)
        {
            int x = (int)Math.Round(positionX);
            int y = (int)Math.Round(positionY);
            int startOffset = (size - 1) / 2;
            int endOffset = size / 2;
            Cv2.Line(preview, new Point(x - startOffset, y), new Point(x + endOffset, y), color, 2);
            Cv2.Line(preview, new Point(x, y - startOffset), new Point(x, y + endOffset), color, 2);
        }

		internal static void DrawLaserPosition(Mat preview, double positionX, double positionY, int size, Scalar color)
		{
			int x = (int)Math.Round(positionX);
			int y = (int)Math.Round(positionY);
			int startOffset = (size - 1) / 2;
			int endOffset = size / 2;

			Cv2.Line(preview, new Point(x - startOffset, y - startOffset),new Point(x + endOffset, y + endOffset), color, 2);
			Cv2.Line(preview, new Point(x - startOffset, y + startOffset),new Point(x + endOffset, y - endOffset), color, 2);
		}
	}
}
