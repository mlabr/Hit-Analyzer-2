using OpenCvSharp;
using System;

namespace HitAnalyzer2.Services.Video
{
    public enum CameraCalibrationMode
    {
        None,
        HitOnly
    }

    internal readonly record struct CalibrationPoint(double X, double Y);

    internal sealed record CalibrationCorners(
        CalibrationPoint TopLeft, CalibrationPoint TopRight,
        CalibrationPoint BottomRight, CalibrationPoint BottomLeft)
    {
        internal static CalibrationCorners Default { get; } =
            new(new(0.1, 0.1), new(0.9, 0.1), new(0.9, 0.9), new(0.1, 0.9));
    }

    internal sealed record CameraCalibrationSettings
    {
        public CameraCalibrationMode Mode { get; init; }
        public CalibrationCorners? Corners { get; init; }
    }

    internal sealed class CameraCalibrationService
    {
        private const double MinimumCornerCrossProduct = 0.0001;
        private volatile CalibrationSnapshot _snapshot;

        internal CameraCalibrationService(CameraCalibrationSettings settings) => _snapshot = CreateSnapshot(settings);

        internal CameraCalibrationSettings Settings => _snapshot.Settings;
        internal bool IsValid => _snapshot.Matrix is not null;

        internal void Update(CameraCalibrationSettings settings) => _snapshot = CreateSnapshot(settings);

        // Both input and output are relative coordinates with the origin at the top left.
        internal CalibrationPoint? MapHit(double x, double y)
        {
            var matrix = _snapshot.Matrix;
            if (matrix is null)
                return null;

            double w = matrix[6] * x + matrix[7] * y + matrix[8];
            if (Math.Abs(w) < 1e-10)
                return null;

            double targetX = (matrix[0] * x + matrix[1] * y + matrix[2]) / w;
            double targetY = (matrix[3] * x + matrix[4] * y + matrix[5]) / w;
            return double.IsFinite(targetX) && double.IsFinite(targetY) ? new(targetX, targetY) : null;
        }

        private static CalibrationSnapshot CreateSnapshot(CameraCalibrationSettings settings)
        {
            if (settings.Mode != CameraCalibrationMode.HitOnly)
                return new(settings, null);

            var corners = settings.Corners ?? CalibrationCorners.Default;
            CalibrationPoint[] points = [corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft];
            var source = new Point2f[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                var c = points[(i + 2) % points.Length];
                double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (!double.IsFinite(cross) || cross <= MinimumCornerCrossProduct ||
                    a.X is < 0 or > 1 || a.Y is < 0 or > 1)
                    return new(settings, null);

                source[i] = new Point2f((float)a.X, (float)a.Y);
            }

            Point2f[] target = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
            using var transform = Cv2.GetPerspectiveTransform(source, target);
            var matrix = new double[9];
            for (int i = 0; i < matrix.Length; i++)
            {
                matrix[i] = transform.At<double>(i / 3, i % 3);
                if (!double.IsFinite(matrix[i]))
                    return new(settings, null);
            }

            return new(settings, matrix);
        }

        private sealed record CalibrationSnapshot(CameraCalibrationSettings Settings, double[]? Matrix);
    }
}
