using HitAnalyzer2.Models;
using OpenCvSharp;
using System;
using System.Threading;

namespace HitAnalyzer2.Services.Target
{
    internal sealed class TargetCalibrationService
    {
        internal static readonly Position TargetCenter = new(0.5, 0.5);
        internal const double TargetRadius = 0.5;
        private const int OutlinePointCount = 128;
        private const double MinimumCornerCrossProduct = 0.0001;
        private volatile CalibrationSnapshot _snapshot;

        internal TargetCalibrationService(TargetPreviewModel settings) => _snapshot = CreateSnapshot(settings);

        internal TargetPreviewModel Settings => _snapshot.Settings;
        internal double? PreviewMmToPixel
        {
            get
            {
                var snapshot = _snapshot;
                int size = Math.Min(snapshot.FrameWidth, snapshot.FrameHeight);
                return size > 1
                    ? snapshot.Settings.MaxDiameterMm / TargetPreviewModel.GetPreviewDiameterPixels(size)
                    : null;
            }
        }

        internal bool IsValid => _snapshot.Matrix is not null;
        internal Position? CameraTargetCenter => _snapshot.CameraTargetCenter;
        internal ReadOnlySpan<Position> CameraTargetOutline => _snapshot.CameraTargetOutline;
        internal event Action? CalibrationGeometryChanged;

        internal void SetTarget(TargetModel target) => UpdateTarget(target, resetCalibration: false);

        internal void ResetCalibration(TargetModel? target = null) => UpdateTarget(target, resetCalibration: true);

        private void UpdateTarget(TargetModel? target, bool resetCalibration)
        {
            CalibrationSnapshot snapshot;
            CalibrationSnapshot updated;
            do
            {
                snapshot = _snapshot;
                var settings = target is null ? snapshot.Settings : new TargetPreviewModel(target);
                if (resetCalibration)
                {
                    var corners = snapshot.FrameWidth > 1 && snapshot.FrameHeight > 1
                        ? CalibrationCorners.CenteredSquare(snapshot.FrameWidth, snapshot.FrameHeight)
                        : null;
                    updated = CreateSnapshot(settings with { Corners = corners })
                        .WithFrameGeometry(snapshot.FrameWidth, snapshot.FrameHeight);
                }
                else
                {
                    updated = snapshot with
                    {
                        Settings = settings with
                        {
                            Corners = snapshot.Settings.Corners,
                            CameraTargetDiameterPixels = snapshot.Settings.CameraTargetDiameterPixels
                        }
                    };
                }
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, updated, snapshot), snapshot));
            CalibrationGeometryChanged?.Invoke();
        }

        internal void SetCorners(CalibrationCorners? corners)
        {
            CalibrationSnapshot snapshot;
            CalibrationSnapshot updated;
            do
            {
                snapshot = _snapshot;
                updated = CreateSnapshot(snapshot.Settings with { Corners = corners })
                    .WithFrameGeometry(snapshot.FrameWidth, snapshot.FrameHeight);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, updated, snapshot), snapshot));
            CalibrationGeometryChanged?.Invoke();
        }

        internal void UpdatePreviewGeometry(int width, int height)
        {
            var snapshot = _snapshot;
            bool hasFrame = width > 1 && height > 1;
            if (snapshot.FrameWidth == width && snapshot.FrameHeight == height &&
                (!hasFrame || snapshot.Settings.Corners is not null))
                return;

            var calibration = hasFrame && snapshot.Settings.Corners is null
                ? CreateSnapshot(snapshot.Settings with { Corners = CalibrationCorners.CenteredSquare(width, height) })
                : snapshot;
            var updated = calibration.WithFrameGeometry(width, height);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, updated, snapshot), snapshot))
                CalibrationGeometryChanged?.Invoke();
        }

        // Both input and output are relative coordinates with the origin at the top left.
        internal Position? MapHit(double x, double y) => Project(_snapshot.Matrix, x, y);

        private static Position? Project(double[]? matrix, double x, double y)
        {
            if (matrix is null)
                return null;

            double w = matrix[6] * x + matrix[7] * y + matrix[8];
            if (Math.Abs(w) < 1e-10)
                return null;

            double targetX = (matrix[0] * x + matrix[1] * y + matrix[2]) / w;
            double targetY = (matrix[3] * x + matrix[4] * y + matrix[5]) / w;
            return double.IsFinite(targetX) && double.IsFinite(targetY) ? new(targetX, targetY) : null;
        }

        private static CalibrationSnapshot CreateSnapshot(TargetPreviewModel settings)
        {
            if (settings.Corners is not { } corners)
                return new(settings, null, null, []);

            Position[] points = [corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft];
            var source = new Point2f[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                var c = points[(i + 2) % points.Length];
                double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (!double.IsFinite(cross) || cross <= MinimumCornerCrossProduct ||
                    a.X is < 0 or > 1 || a.Y is < 0 or > 1)
                    return new(settings, null, null, []);

                source[i] = new Point2f((float)a.X, (float)a.Y);
            }

            Point2f[] target = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
            using var transform = Cv2.GetPerspectiveTransform(source, target);
            using var inverse = new Mat();
            if (Cv2.Invert(transform, inverse) == 0)
                return new(settings, null, null, []);

            var matrix = new double[9];
            var inverseMatrix = new double[9];
            for (int i = 0; i < matrix.Length; i++)
            {
                matrix[i] = transform.At<double>(i / 3, i % 3);
                inverseMatrix[i] = inverse.At<double>(i / 3, i % 3);
                if (!double.IsFinite(matrix[i]) || !double.IsFinite(inverseMatrix[i]))
                    return new(settings, null, null, []);
            }

            var center = Project(inverseMatrix, TargetCenter.X, TargetCenter.Y);
            var outline = new Position[OutlinePointCount];
            for (int i = 0; i < outline.Length; i++)
            {
                double angle = Math.Tau * i / outline.Length;
                if (Project(inverseMatrix, TargetCenter.X + TargetRadius * Math.Cos(angle),
                    TargetCenter.Y + TargetRadius * Math.Sin(angle)) is not { } point)
                    return new(settings, null, null, []);

                outline[i] = point;
            }

            return new(settings, matrix, center, outline);
        }

        private sealed record CalibrationSnapshot(
            TargetPreviewModel Settings, double[]? Matrix, Position? CameraTargetCenter,
            Position[] CameraTargetOutline, int FrameWidth = 0, int FrameHeight = 0)
        {
            internal CalibrationSnapshot WithFrameGeometry(int width, int height)
            {
                double? diameter = null;
                if (width > 1 && height > 1 && CameraTargetOutline.Length == OutlinePointCount)
                {
                    // Average the two projected diameters along the target's axes.
                    int quarter = OutlinePointCount / 4;
                    diameter = (DistanceInPixels(CameraTargetOutline[0], CameraTargetOutline[2 * quarter]) +
                        DistanceInPixels(CameraTargetOutline[quarter], CameraTargetOutline[3 * quarter])) / 2;
                }

                return this with
                {
                    FrameWidth = width,
                    FrameHeight = height,
                    Settings = Settings with { CameraTargetDiameterPixels = diameter }
                };

                double DistanceInPixels(Position a, Position b) =>
                    double.Hypot((a.X - b.X) * width, (a.Y - b.Y) * height);
            }
        }
    }
}
