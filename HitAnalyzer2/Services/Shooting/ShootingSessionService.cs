using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Threading;

using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Services.Video;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class ShootingSessionService(ILaserDetector laserDetector, AimPositionHandler aimPositionHandler,
        DetectionSettings detectionSettings, CameraCalibrationService calibration, long appStartedTimestampMs)
    {
        internal const int NextDetectionMinimumInterval = 100;
        internal const int NextDetectionMaximumInterval = 5000;
        private static readonly TimeSpan MinimumTimeoutAimPathDuration = TimeSpan.FromSeconds(2);

        private readonly object _hitGate = new();
        private volatile ILaserDetector _laserDetector = laserDetector;
        private volatile int _nextDetectionInterval = NextDetectionMinimumInterval;
        private LaserPosition? _lastHit;
        private LaserPosition[] _lastHitAimPath = Array.Empty<LaserPosition>();
        private LaserPosition[] _lastHitFollowPath = Array.Empty<LaserPosition>();
        private TriggerTypeEnum _trigger = TriggerTypeEnum.FirstSpot;
        private bool _spotWasVisible;
        private long? _lastAcceptedExternalTriggerEventTimestamp;
        private long? _externalPressedAt;
        private long _externalReleasedAt;

        private TimeSpan NextDetectionTimestamp { get; set; }

        // Raised on the capture thread. The target point is present only with a valid calibration.
        internal event Action<LaserPosition?, CalibrationPoint?, TimeSpan>? LaserPositionEvaluated;

        internal void SetLaserDetector(ILaserDetector detector) => _laserDetector = detector;

        internal void SetTrigger(TriggerTypeEnum trigger)
        {
            lock (_hitGate)
            {
                bool resetTimeoutPath = _trigger == TriggerTypeEnum.TimeOut || trigger == TriggerTypeEnum.TimeOut;
                if (resetTimeoutPath)
                    aimPositionHandler.ClearAimPaths();

                _trigger = trigger;
                _spotWasVisible = false;
                _externalPressedAt = null;
                if (trigger != TriggerTypeEnum.ExternalEvent || resetTimeoutPath)
                {
                    _lastHitAimPath = Array.Empty<LaserPosition>();
                    _lastHitFollowPath = Array.Empty<LaserPosition>();
                    aimPositionHandler.ClearFollowPath();
                }
            }
        }

        internal void OnExternalTriggerChanged(bool isPressed, long timestamp)
        {
            lock (_hitGate)
            {
                if (isPressed)
                {
                    if (_trigger == TriggerTypeEnum.ExternalEvent &&
                        (_lastAcceptedExternalTriggerEventTimestamp is not { } previousPress ||
                         Stopwatch.GetElapsedTime(previousPress, timestamp) >=
                         TimeSpan.FromMilliseconds(NexDetectionInterval)))
                    {
                        _lastAcceptedExternalTriggerEventTimestamp = timestamp;
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
            set => _nextDetectionInterval = Math.Clamp(value, NextDetectionMinimumInterval, NextDetectionMaximumInterval);
        }

        internal void ClearAimPaths()
        {
            lock (_hitGate)
            {
                aimPositionHandler.ClearAimPaths();
                _lastHit = null;
                _lastHitAimPath = Array.Empty<LaserPosition>();
                _lastHitFollowPath = Array.Empty<LaserPosition>();
            }
        }

        internal (AimPosition? Hit, AimPosition? CurrentAim, LaserPosition[] AimPath, LaserPosition[] FollowPath) ProcessFrame(
            Mat frame, long frameCapturedTimestampMs, double detectedFps, CancellationToken cancellationToken)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(appStartedTimestampMs, frameCapturedTimestampMs);
            LaserPosition? registeredHit = null;
            LaserPosition? lastHit;
            LaserPosition? position;
            LaserPosition[] aimPath;
            LaserPosition[] followPath;
            lock (_hitGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var detectedPosition = _laserDetector.Detect(frame);
                position = detectedPosition is { } point ? point with { Time = elapsed } : null;
                bool startFollowPath = false;
                if (_trigger == TriggerTypeEnum.TimeOut && position is null &&
                    aimPositionHandler.GetTimedOutAimPosition(elapsed) is { } timedOutPosition)
                {
                    if (aimPositionHandler.AimPathDuration >= MinimumTimeoutAimPathDuration &&
                        IsAimPositionInsideAllowedZone(aimPositionHandler.GetAimPosition(timedOutPosition), frame))
                    {
                        registeredHit = timedOutPosition;
                        _lastHitAimPath = aimPositionHandler.GetAimPathSnapshot();
                    }

                    aimPositionHandler.ClearAimPath();
                }
                else
                {
                    aimPositionHandler.UpdateAimPath(position, elapsed, detectedFps);
                    if (_trigger != TriggerTypeEnum.TimeOut &&
                        IsTriggered(position, frameCapturedTimestampMs, elapsed >= NextDetectionTimestamp))
                    {
                        registeredHit = position;
                        if (_trigger == TriggerTypeEnum.ExternalEvent)
                        {
                            _externalPressedAt = null;
                            _lastHitAimPath = aimPositionHandler.GetAimPathSnapshot();
                            startFollowPath = true;
                        }
                        else
                        {
                            NextDetectionTimestamp = elapsed + TimeSpan.FromMilliseconds(NexDetectionInterval);
                        }
                    }
                }

                if (registeredHit.HasValue)
                    _lastHit = registeredHit;

                lastHit = _lastHit;
                aimPath = _lastHitAimPath;
                var updatedFollowPath = aimPositionHandler.UpdateFollowPath(position, elapsed, detectedFps, startFollowPath);
                if (startFollowPath || updatedFollowPath.Length > 0)
                    _lastHitFollowPath = updatedFollowPath;
                followPath = _lastHitFollowPath;
            }

            CalibrationPoint? targetPoint = null;
            if (registeredHit is { } registered)
            {
                var aim = aimPositionHandler.GetAimPosition(registered);
                targetPoint = calibration.MapHit(aim.X / frame.Width, aim.Y / frame.Height);
            }

            LaserPositionEvaluated?.Invoke(registeredHit, targetPoint, elapsed);

            if (registeredHit is { } hit)
                Console.WriteLine($"Laser detected at ({hit.X}, {hit.Y})");

            return (lastHit is { } lastPosition ? aimPositionHandler.GetAimPosition(lastPosition) : null,
                position is { } current ? aimPositionHandler.GetAimPosition(current) : null, aimPath, followPath);
        }

        private bool IsAimPositionInsideAllowedZone(AimPosition position, Mat frame)
        {
            var allowedArea = AllowedArea.GetBounds(frame, detectionSettings);
            return position.X >= allowedArea.Left && position.X < allowedArea.Right &&
                position.Y >= allowedArea.Top && position.Y < allowedArea.Bottom;
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
