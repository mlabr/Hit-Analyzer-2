using OpenCvSharp;
using System;
using System.Diagnostics;

using HitAnalyzer2.Services.Detection;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class ShootingSessionService(ILaserDetector laserDetector, long startedAt)
    {
        internal const int MinimumDetectionInterval = 100;
        internal const int MaximumDetectionInterval = 5000;

        private readonly object _hitGate = new();
        private volatile ILaserDetector _laserDetector = laserDetector;
        private volatile int _nextDetectionInterval = MinimumDetectionInterval;
        private LaserPosition? _lastHit;

        private TimeSpan NextDetectionTimeStamp { get; set; }

        // Raised on the capture thread. A position is present only for a newly registered hit.
        internal event Action<LaserPosition?, TimeSpan>? LaserPositionEvaluated;

        internal void SetLaserDetector(ILaserDetector detector) => _laserDetector = detector;

        internal int NexDetectionInterval
        {
            get => _nextDetectionInterval;
            set => _nextDetectionInterval = Math.Clamp(value, MinimumDetectionInterval, MaximumDetectionInterval);
        }

        internal LaserPosition? ProcessFrame(Mat frame, long capturedAt)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt, capturedAt);
            LaserPosition? registeredHit = null;
            LaserPosition? lastHit;
            lock (_hitGate)
            {
                if (elapsed >= NextDetectionTimeStamp && _laserDetector.Detect(frame) is { } position)
                {
                    registeredHit = position with { Time = elapsed };
                    _lastHit = registeredHit;
                    NextDetectionTimeStamp = elapsed + TimeSpan.FromMilliseconds(NexDetectionInterval);
                }
                lastHit = _lastHit;
            }

            LaserPositionEvaluated?.Invoke(registeredHit, elapsed);

            if (registeredHit is { } hit)
                Console.WriteLine($"Laser detected at ({hit.X}, {hit.Y})");

            return lastHit;
        }
    }
}
