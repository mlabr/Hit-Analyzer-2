using OpenCvSharp;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class LaserDetectorBlob(
        int intensityThreshold, int minimumHitAreaPixels) : ILaserDetector
    {
        public LaserPosition? Detect(Mat frame)
        {
            if (frame.Empty())
                return null;

            using var intensity = new Mat();
            Mat pixels = frame;
            if (frame.Type() == MatType.CV_8UC3 || frame.Type() == MatType.CV_8UC4)
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
            else if (frame.Type() != MatType.CV_8UC1)
            {
                return null;
            }

            using var mask = new Mat();
            Cv2.InRange(pixels, Scalar.All(intensityThreshold), Scalar.All(byte.MaxValue), mask);
            if (Cv2.CountNonZero(mask) < minimumHitAreaPixels)
                return null;

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int componentCount = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);
            int selected = 0;
            int largestArea = 0;
            for (int label = 1; label < componentCount; label++)
            {
                int area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
                if (area >= minimumHitAreaPixels && area > largestArea)
                {
                    selected = label;
                    largestArea = area;
                }
            }

            return selected == 0
                ? null
                : new LaserPosition(centroids.At<double>(selected, 0), centroids.At<double>(selected, 1));
        }
    }
}
