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
        private HitTrigger _trigger = HitTrigger.FirstSpot;
        private bool _spotWasVisible;

        private TimeSpan NextDetectionTimeStamp { get; set; }

        // Raised on the capture thread. A position is present only for a newly registered hit.
        internal event Action<LaserPosition?, TimeSpan>? LaserPositionEvaluated;

        internal void SetLaserDetector(ILaserDetector detector) => _laserDetector = detector;

        internal void SetTrigger(HitTrigger trigger)
        {
            lock (_hitGate)
            {
                _trigger = trigger;
                _spotWasVisible = false;
            }
        }

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
                bool intervalElapsed = elapsed >= NextDetectionTimeStamp;
                if ((intervalElapsed || _trigger == HitTrigger.NewSpot)
                    && GetTriggeredPosition(frame, intervalElapsed) is { } position)
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

        private LaserPosition? GetTriggeredPosition(Mat frame, bool intervalElapsed)
        {
            LaserPosition? position = _laserDetector.Detect(frame);
            if (_trigger == HitTrigger.FirstSpot)
                return position;

            // Observe every frame so a spot that appeared during the interval is not a new spot afterward.
            bool appeared = position.HasValue && !_spotWasVisible;
            _spotWasVisible = position.HasValue;
            return appeared && intervalElapsed ? position : null;
        }
    }
}
