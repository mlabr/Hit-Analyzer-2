using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Threading;

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
        private LaserPosition[] _lastHitAimPath = Array.Empty<LaserPosition>();
        private LaserPosition[] _lastHitFollowPath = Array.Empty<LaserPosition>();
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
                if (trigger != TriggerTypeEnum.ExternalEvent)
                {
                    _lastHitAimPath = Array.Empty<LaserPosition>();
                    _lastHitFollowPath = Array.Empty<LaserPosition>();
                    aimPositionHandler.ClearFollowPath();
                }
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

        internal void ClearAimPaths()
        {
            lock (_hitGate)
            {
                aimPositionHandler.ClearAimPaths();
                _lastHitAimPath = Array.Empty<LaserPosition>();
                _lastHitFollowPath = Array.Empty<LaserPosition>();
            }
        }

        internal (AimPosition? Hit, LaserPosition[] AimPath, LaserPosition[] FollowPath) ProcessFrame(
            Mat frame, long capturedAt, double detectedFps, CancellationToken cancellationToken)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt, capturedAt);
            LaserPosition? registeredHit = null;
            LaserPosition? lastHit;
            LaserPosition[] aimPath;
            LaserPosition[] followPath;
            lock (_hitGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var detectedPosition = _laserDetector.Detect(frame);
                LaserPosition? position = detectedPosition is { } point ? point with { Time = elapsed } : null;
                aimPositionHandler.UpdateAimPath(position, elapsed, detectedFps);
                bool intervalElapsed = elapsed >= NextDetectionTimeStamp;
                bool externalHit = false;
                if (IsTriggered(position, capturedAt, intervalElapsed))
                {
                    if (_trigger == TriggerTypeEnum.ExternalEvent)
                    {
                        _externalPressedAt = null;
                        _lastHitAimPath = aimPositionHandler.GetAimPathSnapshot();
                        externalHit = true;
                    }
                    else
                        NextDetectionTimeStamp = elapsed + TimeSpan.FromMilliseconds(NexDetectionInterval);

                    registeredHit = position;
                    _lastHit = registeredHit;
                }
                lastHit = _lastHit;
                aimPath = _lastHitAimPath;
                var updatedFollowPath = aimPositionHandler.UpdateFollowPath(position, elapsed, detectedFps, externalHit);
                if (externalHit || updatedFollowPath.Length > 0)
                    _lastHitFollowPath = updatedFollowPath;
                followPath = _lastHitFollowPath;
            }

            LaserPositionEvaluated?.Invoke(registeredHit, elapsed);

            if (registeredHit is { } hit)
                Console.WriteLine($"Laser detected at ({hit.X}, {hit.Y})");

            return (lastHit is { } lastPosition ? aimPositionHandler.GetAimPosition(lastPosition) : null, aimPath, followPath);
        }

        private bool IsTriggered(LaserPosition? position, long capturedAt, bool intervalElapsed)
        {
            if (_trigger == TriggerTypeEnum.ExternalEvent)
            {
                // Compare against capture time, even if the frame is processed after key-up.
                return position.HasValue && _externalPressedAt is { } pressedAt &&
                    capturedAt >= pressedAt && capturedAt < _externalReleasedAt;
            }

            if (_trigger == TriggerTypeEnum.FirstSpot)
                return position.HasValue && intervalElapsed;

            // Observe every frame so a spot that appeared during the interval is not a new spot afterward.
            bool appeared = position.HasValue && !_spotWasVisible;
            _spotWasVisible = position.HasValue;
            return appeared && intervalElapsed;
        }
    }
}
