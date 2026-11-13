using OpenCvSharp;
using System;

namespace HitAnalyzer2.Services.Detection
{
    internal interface ILaserDetector
    {
        LaserPosition? GetLaserPosition(Mat frame, TimeSpan frameTime);
    }
}
