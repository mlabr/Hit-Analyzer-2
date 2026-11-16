using HitAnalyzer2.Services.Target;

namespace HitAnalyzer2.Models
{
    internal sealed record TargetModel
    {
        internal const double PreviewMargin = 0.25;
        private const double PreviewSpan = 1 + 2 * PreviewMargin;

        public CalibrationCorners? Corners { get; init; }
        internal double MaxDiameterMm => 120;
        internal int PreviewSizePixels { get; init; }

        // Physical millimeters per pixel in the corrected preview bitmap.
        internal double? MmToPixel => PreviewSizePixels > 1
            ? MaxDiameterMm / GetPreviewDiameterPixels(PreviewSizePixels)
            : null;

        internal static double GetPreviewDiameterPixels(int size) => (size - 1) / PreviewSpan;
    }
}
