using OpenCvSharp;
using System;
using System.Diagnostics;

using HitAnalyzer2.Services.Detection;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class ShootingSessionService(ILaserDetector laserDetector, AimPositionHandler aimPositionHandler, long startedAt)
    {
        internal const int MinimumDetectionInterval = 100;
        internal const int MaximumDetectionInterval = 5000;

        private readonly object _hitGate = new();
        private volatile ILaserDetector _laserDetector = laserDetector;
        private volatile int _nextDetectionInterval = MinimumDetectionInterval;
        private LaserPosition? _lastHit;
        private TriggerTypeEnum _trigger = TriggerTypeEnum.FirstSpot;
        private bool _spotWasVisible;
        private long? _lastAcceptedExternalPressAt;
        private long? _externalPressedAt;
        private long _externalReleasedAt;

        private TimeSpan NextDetectionTimeStamp { get; set; }

        // Raised on the capture thread. A position is present only for a newly registered hit.
        internal event Action<LaserPosition?, TimeSpan>? LaserPositionEvaluated;

        internal void SetLaserDetector(ILaserDetector detector) => _laserDetector = detector;

        internal void SetTrigger(TriggerTypeEnum trigger)
        {
            lock (_hitGate)
            {
                _trigger = trigger;
                _spotWasVisible = false;
                _externalPressedAt = null;
            }
        }

        internal void OnExternalPressChanged(bool isPressed, long timestamp)
        {
            lock (_hitGate)
            {
                if (isPressed)
                {
                    if (_trigger == TriggerTypeEnum.ExternalEvent &&
                        (_lastAcceptedExternalPressAt is not { } previousPress ||
                         Stopwatch.GetElapsedTime(previousPress, timestamp) >=
                         TimeSpan.FromMilliseconds(NexDetectionInterval)))
                    {
                        _lastAcceptedExternalPressAt = timestamp;
                        _externalPressedAt = timestamp;
                        _externalReleasedAt = long.MaxValue;
                    }
                }
                else if (_externalReleasedAt == long.MaxValue)
                {
                    _externalReleasedAt = timestamp;
                }
            }
        }

        internal int NexDetectionInterval
        {
            get => _nextDetectionInterval;
            set => _nextDetectionInterval = Math.Clamp(value, MinimumDetectionInterval, MaximumDetectionInterval);
        }

        internal AimPosition? ProcessFrame(Mat frame, long capturedAt)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt, capturedAt);
            LaserPosition? registeredHit = null;
            LaserPosition? lastHit;
            lock (_hitGate)
            {
                bool intervalElapsed = elapsed >= NextDetectionTimeStamp;
                if ((intervalElapsed || _trigger != TriggerTypeEnum.FirstSpot)
                    && GetTriggeredPosition(frame, capturedAt, intervalElapsed) is { } position)
                {
                    if (_trigger == TriggerTypeEnum.ExternalEvent)
                        _externalPressedAt = null;
                    else
                        NextDetectionTimeStamp = elapsed + TimeSpan.FromMilliseconds(NexDetectionInterval);

                    registeredHit = position with { Time = elapsed };
                    _lastHit = registeredHit;
                }
                lastHit = _lastHit;
            }

            LaserPositionEvaluated?.Invoke(registeredHit, elapsed);

            if (registeredHit is { } hit)
                Console.WriteLine($"Laser detected at ({hit.X}, {hit.Y})");

            return lastHit is { } lastPosition ? aimPositionHandler.GetAimPosition(lastPosition) : null;
        }

        private LaserPosition? GetTriggeredPosition(Mat frame, long capturedAt, bool intervalElapsed)
        {
            if (_trigger == TriggerTypeEnum.ExternalEvent)
            {
                // Compare against capture time, even if the frame is processed after key-up.
                if (_externalPressedAt is not { } pressedAt || capturedAt < pressedAt ||
                    capturedAt >= _externalReleasedAt)
                    return null;

                return _laserDetector.Detect(frame);
            }

            LaserPosition? position = _laserDetector.Detect(frame);
            if (_trigger == TriggerTypeEnum.FirstSpot)
                return position;

            // Observe every frame so a spot that appeared during the interval is not a new spot afterward.
            bool appeared = position.HasValue && !_spotWasVisible;
            _spotWasVisible = position.HasValue;
            return appeared && intervalElapsed ? position : null;
        }
    }
}
