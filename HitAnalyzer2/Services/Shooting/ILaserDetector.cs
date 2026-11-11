using OpenCvSharp;

namespace HitAnalyzer2.Services.Shooting
{
    internal readonly record struct LaserPosition(double X, double Y)
    {
        public System.TimeSpan Time { get; init; }
    }

    internal interface ILaserDetector
    {
        LaserPosition? Detect(Mat frame);
    }
}
