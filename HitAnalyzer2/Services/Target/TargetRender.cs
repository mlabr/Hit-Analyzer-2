using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Services.Shooting;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace HitAnalyzer2.Services.Target
{
    internal static class TargetRender
    {
        private const double PreviewMargin = 0.25;
        private const double PreviewSpan = 1 + 2 * PreviewMargin;

        internal static void DrawTargetPreview(Mat preview, AimPosition? aim, AimPosition? lastHit,
            IReadOnlyList<LaserPosition> aimPath, IReadOnlyList<LaserPosition> followPath,
            TargetHit? lastTargetHit, TargetCalibrationService calibration)
        {
            preview.SetTo(new Scalar(211, 211, 211, 255));
            int size = Math.Min(preview.Width, preview.Height);
            if (size < 2)
                return;

            using var target = new Mat(preview, new Rect((preview.Width - size) / 2, (preview.Height - size) / 2, size, size));
            var guide = new Scalar(0, 0, 0, 255);
            Cv2.Rectangle(target, default(Point), new Point(size - 1, size - 1), guide);
            var center = ToTargetPixel(TargetCalibrationService.TargetCenter);
            int radius = (int)Math.Round(TargetCalibrationService.TargetRadius * (size - 1) / PreviewSpan);
            Cv2.Circle(target, center, radius, new Scalar(255, 255, 255, 255), -1);
            Cv2.Circle(target, center, radius, guide);
            Cv2.Circle(target, center, 2, guide, -1);

            var reference = aim ?? lastHit;
            var offset = reference is { } position
                ? (position.X - position.LaserPosition.X, position.Y - position.LaserPosition.Y)
                : (0.0, 0.0);
            AimRenderer.DrawAimPath(target, aimPath, AimRenderer.AimPathBeforeColor, offset, false, Project);
            AimRenderer.DrawAimPath(target, followPath, AimRenderer.AimPathAfterColor, offset, true, Project);

            int markerSize = Math.Max(1, (int)Math.Round(target.Height * AimRenderer.MarkerSizeRatio));
            if (lastTargetHit is { } hit)
            {
                if (hit.Laser is { } laser && IsInsidePreview(laser))
                {
                    var marker = ToTargetPixel(laser);
                    AimRenderer.DrawLaserPosition(target, marker.X, marker.Y, markerSize, AimRenderer.LaserPositionMarkerColor);
                }

                if (hit.Aim is { } hitAim && IsInsidePreview(hitAim))
                {
                    var marker = ToTargetPixel(hitAim);
                    AimRenderer.DrawAimPosition(target, marker.X, marker.Y, markerSize, AimRenderer.AimPositionMarkerColor);
                }
            }

            if (aim is { } current)
            {
                var laser = current.LaserPosition;
                if ((current.X != laser.X || current.Y != laser.Y) && Project(laser.X, laser.Y) is { } laserMarker)
                    AimRenderer.DrawLaserPosition(target, laserMarker.X, laserMarker.Y, markerSize, AimRenderer.LaserPositionMarkerColor);

                if (Project(current.X, current.Y) is { } aimMarker)
                    AimRenderer.DrawAimPosition(target, aimMarker.X, aimMarker.Y, markerSize, AimRenderer.AimPositionMarkerColor);
            }

            Point ToTargetPixel(CalibrationPoint point) =>
                new(Math.Round((point.X + PreviewMargin) * (size - 1) / PreviewSpan),
                    Math.Round((point.Y + PreviewMargin) * (size - 1) / PreviewSpan));

            Point? Project(double x, double y)
            {
                var mapped = calibration.MapHit(x / preview.Width, y / preview.Height);
                return mapped is { } point && IsInsidePreview(point) ? ToTargetPixel(point) : null;
            }
        }

        private static bool IsInsidePreview(CalibrationPoint point)
        {
            const double min = -PreviewMargin - 0.000001;
            const double max = 1 + PreviewMargin + 0.000001;
            return point is { X: >= min and <= max, Y: >= min and <= max };
        }
    }
}
