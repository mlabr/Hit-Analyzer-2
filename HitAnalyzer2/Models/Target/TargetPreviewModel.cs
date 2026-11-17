namespace HitAnalyzer2.Models
{
    internal sealed record TargetPreviewModel : TargetModel
    {
        internal const double PreviewMargin = 0.25;
        private const double PreviewSpan = 1 + 2 * PreviewMargin;

        public TargetPreviewModel() { }

        internal TargetPreviewModel(TargetModel target) : base(target) { }

        public CalibrationCorners? Corners { get; init; }
        internal double? CameraTargetDiameterPixels { get; init; }

        // Physical millimeters per pixel at the calibrated target in the camera frame.
        internal double? MmToPixel => CameraTargetDiameterPixels is > 0
            ? MaxDiameterMm / CameraTargetDiameterPixels
            : null;

        internal static double GetPreviewDiameterPixels(int size) => (size - 1) / PreviewSpan;
    }
}
