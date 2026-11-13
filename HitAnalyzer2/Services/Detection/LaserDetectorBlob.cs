using OpenCvSharp;
using System;

namespace HitAnalyzer2.Services.Detection
{
    internal sealed class LaserDetectorBlob(
        int intensityThreshold, int minimumHitAreaPixels) : ILaserDetector
    {
        public LaserPosition? GetLaserPosition(Mat frame, TimeSpan frameTime)
        {
            if (frame.Empty())
                return null;

            MatType type = frame.Type();
            if (type != MatType.CV_8UC1 && type != MatType.CV_8UC3 && type != MatType.CV_8UC4)
                return null;

            using var mask = new Mat();
            LaserIntensity.CreateMask(frame, mask, intensityThreshold);
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
                : new LaserPosition(centroids.At<double>(selected, 0), centroids.At<double>(selected, 1), frameTime);
        }
    }
}
