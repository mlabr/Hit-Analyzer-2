using OpenCvSharp;
using System;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class LaserDetectorSimple(int intensityThreshold) : ILaserDetector
    {
        public unsafe LaserPosition? Detect(Mat frame)
        {
            if (frame.Empty())
                return null;

            MatType type = frame.Type();
            if (type != MatType.CV_8UC1 && type != MatType.CV_8UC3 && type != MatType.CV_8UC4)
                return null;

            int channels = frame.Channels();
            int width = frame.Cols;
            int height = frame.Rows;
            int brightest = -1;
            Point first = default;
            Point last = default;

            for (int y = 0; y < height; y++)
            {
                byte* pixel = (byte*)frame.Ptr(y);
                for (int x = 0; x < width; x++, pixel += channels)
                {
                    int intensity = channels == 1
                        ? pixel[0]
                        : Math.Max(pixel[0], Math.Max(pixel[1], pixel[2]));

                    if (intensity < intensityThreshold || intensity < brightest)
                        continue;

                    last = new Point(x, y);
                    if (intensity > brightest)
                    {
                        brightest = intensity;
                        first = last;
                    }
                }
            }

            return brightest < 0
                ? null
                : new LaserPosition(first.X + (last.X - first.X) / 2.0,
                    first.Y + (last.Y - first.Y) / 2.0);
        }
    }
}
