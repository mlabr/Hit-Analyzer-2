using HitAnalyzer2.Enums;
using HitAnalyzer2.Services.Target;
using System.Collections.Generic;
using System.Linq;

namespace HitAnalyzer2.Models
{
    internal sealed record TargetModel
    {
        internal const double PreviewMargin = 0.25;
        private const double PreviewSpan = 1 + 2 * PreviewMargin;

        public CalibrationCorners? Corners { get; init; }
        internal string? TargetName { get; init; }
        internal TargetTypeEnum TargetType { get; init; }
        internal IReadOnlyList<TargetRingModel> Rings { get; init; } = [];
        internal double? MarkedRingPoints { get; init; }
        internal double? MaxDiameter => Rings.Count == 0 ? null : Rings.Max(ring => ring.Diameter);
        internal double? MaxDiameterMm => MaxDiameter * (TargetType == TargetTypeEnum.CircleImperial ? 25.4 : 1);
        internal double? CameraTargetDiameterPixels { get; init; }

        // Physical millimeters per pixel at the calibrated target in the camera frame.
        internal double? MmToPixel => CameraTargetDiameterPixels is > 0
            ? MaxDiameterMm / CameraTargetDiameterPixels
            : null;

        internal static double GetPreviewDiameterPixels(int size) => (size - 1) / PreviewSpan;

    }
}
