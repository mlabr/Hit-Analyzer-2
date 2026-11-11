using OpenCvSharp;

namespace HitAnalyzer2.Services.Shooting
{
    internal static class LaserIntensity
    {
        internal static int ThresholdFromSensitivity(int sensitivity) => byte.MaxValue - sensitivity;

        internal static void CreateMask(Mat frame, Mat mask, int threshold)
        {
            using var intensity = new Mat();
            Mat pixels = frame;
            if (frame.Channels() > 1)
            {
                using var channel = new Mat();
                Cv2.ExtractChannel(frame, intensity, 0);
                for (int index = 1; index < 3; index++)
                {
                    Cv2.ExtractChannel(frame, channel, index);
                    Cv2.Max(intensity, channel, intensity);
                }

                pixels = intensity;
            }

            Cv2.InRange(pixels, Scalar.All(threshold), Scalar.All(byte.MaxValue), mask);
        }
    }
}
