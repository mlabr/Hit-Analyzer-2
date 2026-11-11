using OpenCvSharp;
using System;
using System.Diagnostics;

using HitAnalyzer2.Services.Detection;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class ShootingSessionService(ILaserDetector laserDetector, long startedAt)
    {
        private volatile ILaserDetector _laserDetector = laserDetector;

        // Raised on the capture thread, once per processed frame.
        internal event Action<LaserPosition?, TimeSpan>? LaserPositionEvaluated;

        internal void SetLaserDetector(ILaserDetector detector) => _laserDetector = detector;

        internal LaserPosition? ProcessFrame(Mat frame, long capturedAt)
        {
            LaserPosition? position = _laserDetector.Detect(frame);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt, capturedAt);
            if (position is { } hit)
                position = hit with { Time = elapsed };

            LaserPositionEvaluated?.Invoke(position, elapsed);

            if(position != null)
            {
                Console.WriteLine($"Laser detected at ({position.Value.X}, {position.Value.Y})");
			}
            return position;
        }
    }
}
