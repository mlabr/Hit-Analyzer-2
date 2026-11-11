using OpenCvSharp;

namespace HitAnalyzer2.Services.Shooting
{
    internal readonly record struct LaserPosition(double X, double Y);

    internal interface ILaserDetector
    {
        LaserPosition? Detect(Mat frame);
    }
}
