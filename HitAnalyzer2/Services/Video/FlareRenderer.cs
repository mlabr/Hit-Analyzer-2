using OpenCvSharp;

namespace HitAnalyzer2.Services.Video
{
    internal static class FlareRenderer
    {
        private const int DotRadius = 2;
        private static readonly Scalar Pink = new(203, 192, 255, 255);

        internal static void Draw(Mat frame, Mat preview, int sensitivity)
        {
            using var intensity = new Mat();
            Mat pixels = frame;
            if (frame.Channels() > 1)
            {
                // Match the laser detectors' intensity and ignore the alpha channel.
                using var channel = new Mat();
                Cv2.ExtractChannel(frame, intensity, 0);
                for (int index = 1; index < 3; index++)
                {
                    Cv2.ExtractChannel(frame, channel, index);
                    Cv2.Max(intensity, channel, intensity);
                }

                pixels = intensity;
            }

            Cv2.MinMaxLoc(pixels, out _, out double brightest);
            using var mask = new Mat();
            Cv2.Threshold(pixels, mask, brightest - sensitivity, byte.MaxValue, ThresholdTypes.Binary);

            // Dilating with a filled circle stamps a 5 px dot at every matching pixel.
            const int diameter = DotRadius * 2 + 1;
            using var dot = new Mat(diameter, diameter, MatType.CV_8UC1, Scalar.All(0));
            Cv2.Circle(dot, new Point(DotRadius, DotRadius), DotRadius, Scalar.All(1), -1);
            Cv2.Dilate(mask, mask, dot);
            preview.SetTo(Pink, mask);
        }
    }
}
