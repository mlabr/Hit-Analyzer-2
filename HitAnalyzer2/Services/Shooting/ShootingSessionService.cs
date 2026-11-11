using OpenCvSharp;
using System;
using System.Diagnostics;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class ShootingSessionService(ILaserDetector laserDetector)
    {
        private readonly long _startedAt = Stopwatch.GetTimestamp();
        private volatile ILaserDetector _laserDetector = laserDetector;

        // Raised on the capture thread, once per processed frame.
        internal event Action<LaserPosition?, TimeSpan>? LaserPositionEvaluated;

        internal void SetLaserDetector(ILaserDetector detector) => _laserDetector = detector;

        internal LaserPosition? ProcessFrame(Mat frame, long capturedAt)
        {
            LaserPosition? position = _laserDetector.Detect(frame);
            LaserPositionEvaluated?.Invoke(position, Stopwatch.GetElapsedTime(_startedAt, capturedAt));

            if(position != null)
            {
                Console.WriteLine($"Laser detected at ({position.Value.X}, {position.Value.Y})");
			}
            return position;
        }
    }
}
