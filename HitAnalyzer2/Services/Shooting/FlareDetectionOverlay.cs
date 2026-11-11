using OpenCvSharp;

namespace HitAnalyzer2.Services.Shooting
{
    internal static class FlareDetectionOverlay
    {
        private const int DotRadius = 4;
        //private static readonly Scalar Pink = new(203, 192, 255, 255);

		private static readonly Scalar Cyan = new(255,255, 0, 255);


        internal static void Draw(Mat preview, int sensitivity)
        {
            using var mask = new Mat();
            LaserIntensity.CreateMask(preview, mask, LaserIntensity.ThresholdFromSensitivity(sensitivity));

            // Dilating with a filled circle stamps a 5 px dot at every matching pixel.
            const int diameter = DotRadius * 2 + 1;
            using var dot = new Mat(diameter, diameter, MatType.CV_8UC1, Scalar.All(0));
            Cv2.Circle(dot, new Point(DotRadius, DotRadius), DotRadius, Scalar.All(1), -1);
            Cv2.Dilate(mask, mask, dot);
            preview.SetTo(Cyan, mask);
        }
    }
}
