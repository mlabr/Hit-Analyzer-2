using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Services.Target;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace HitAnalyzer2.Services.Shooting
{
    internal static class AimRenderer
    {
        private const double MarkerSizeRatio = 0.05;
        private const int MinimumAimPathBrightness = 64;
		private static readonly Scalar AimPositionMarkerColor = new(0, 255, 255, 255);
		private static readonly Scalar LaserPositionMarkerColor = new(255, 0, 0, 255);
        private static readonly Scalar AimPathBeforeColor = new(0, 255, 255, 255);
        private static readonly Scalar AimPathAfterColor = new(255, 255, 0, 255);

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

        internal static void DrawAimPointPreview(Mat preview, AimPosition? aim, AimPosition? lastHit,
            IReadOnlyList<LaserPosition> aimPath, IReadOnlyList<LaserPosition> followPath,
            TargetHit? lastTargetHit, TargetCalibrationService calibration)
        {
            int size = Math.Min(preview.Width, preview.Height);
            if (size < 2)
                return;

            using var target = new Mat(preview, new Rect((preview.Width - size) / 2, (preview.Height - size) / 2, size, size));
            target.SetTo(new Scalar(25, 25, 25, 255));
            var guide = new Scalar(210, 210, 210, 255);
            Cv2.Rectangle(target, default(Point), new Point(size - 1, size - 1), guide);
            var center = ToTargetPixel(TargetCalibrationService.TargetCenter);
            Cv2.Circle(target, center, (size - 1) / 2, guide);
            Cv2.Circle(target, center, 2, guide, -1);

            var reference = aim ?? lastHit;
            var offset = reference is { } position
                ? (position.X - position.LaserPosition.X, position.Y - position.LaserPosition.Y)
                : (0.0, 0.0);
            DrawAimPath(target, aimPath, AimPathBeforeColor, offset, false, Project);
            DrawAimPath(target, followPath, AimPathAfterColor, offset, true, Project);

            int markerSize = Math.Max(1, (int)Math.Round(target.Height * MarkerSizeRatio));
            if (lastTargetHit is { } hit)
            {
                if (hit.Laser is { } laser && IsInsideTarget(laser))
                {
                    var marker = ToTargetPixel(laser);
                    DrawLaserPosition(target, marker.X, marker.Y, markerSize, LaserPositionMarkerColor);
                }

                if (hit.Aim is { } hitAim && IsInsideTarget(hitAim))
                {
                    var marker = ToTargetPixel(hitAim);
                    DrawAimPosition(target, marker.X, marker.Y, markerSize, AimPositionMarkerColor);
                }
            }

            if (aim is { } current)
            {
                var laser = current.LaserPosition;
                if ((current.X != laser.X || current.Y != laser.Y) && Project(laser.X, laser.Y) is { } laserMarker)
                    DrawLaserPosition(target, laserMarker.X, laserMarker.Y, markerSize, LaserPositionMarkerColor);

                if (Project(current.X, current.Y) is { } aimMarker)
                    DrawAimPosition(target, aimMarker.X, aimMarker.Y, markerSize, AimPositionMarkerColor);
            }

            Point ToTargetPixel(CalibrationPoint point) =>
                new(Math.Round(point.X * (size - 1)), Math.Round(point.Y * (size - 1)));

            Point? Project(double x, double y)
            {
                var mapped = calibration.MapHit(x / preview.Width, y / preview.Height);
                return mapped is { } point && IsInsideTarget(point) ? ToTargetPixel(point) : null;
            }
        }

        private static bool IsInsideTarget(CalibrationPoint point) =>
            point is { X: >= -0.000001 and <= 1.000001, Y: >= -0.000001 and <= 1.000001 };

        private static void DrawAimPath(Mat preview, IReadOnlyList<LaserPosition> aimPath, Scalar baseColor,
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

        private static void DrawAimPosition(Mat preview, double positionX, double positionY, int size, Scalar color)
        {
            int x = (int)Math.Round(positionX);
            int y = (int)Math.Round(positionY);
            int startOffset = (size - 1) / 2;
            int endOffset = size / 2;
            Cv2.Line(preview, new Point(x - startOffset, y), new Point(x + endOffset, y), color, 2);
            Cv2.Line(preview, new Point(x, y - startOffset), new Point(x, y + endOffset), color, 2);
        }

		private static void DrawLaserPosition(Mat preview, double positionX, double positionY, int size, Scalar color)
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
