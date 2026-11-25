using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using HitAnalyzer2.Models;
using HitAnalyzer2.Models.Shooting;
using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Services.Recording;
using HitAnalyzer2.Services.Target;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class ShootingSessionService(ILaserDetector laserDetector, AimPositionHandler aimPositionHandler,
        WeaponHandler weaponHandler, ShootingSettings shootingSettings, DetectionSettings detectionSettings, TargetCalibrationService calibration,
        RecordingService recording, long appStartedTimestampMs)
    {
        internal enum SessionEndReason { TimeLimit, HitLimit }

        internal const int NextDetectionMinimumInterval = 100;
        internal const int NextDetectionMaximumInterval = 5000;
        internal SessionLimits SessionLimits { get; } = new()
        {
            IsHitLimitEnabled = shootingSettings.IsHitLimitEnable,
            HitCountInitial = Math.Max(0, shootingSettings.HitLimit),
            IsTimeLimitEnabled = shootingSettings.IsTimeLimitEnable,
            TimeLimitSecondsInitial = Math.Max(1, shootingSettings.TimeLimitSeconds)
        };

        private readonly TimeSpan _aimPathTimeoutMinimumDuration =
            TimeSpan.FromSeconds(shootingSettings.AimPathTimeoutMinimumDuration);

        private readonly object _hitGate = new();
        private volatile ILaserDetector _laserDetector = laserDetector;
        private volatile int _nextDetectionInterval = NextDetectionMinimumInterval;
        private LaserPosition? _lastHit;
        private double _lastHitCaliberDiameterMm;
        private Position? _lastTargetHit;
        private LaserPosition[] _lastHitAimPath = Array.Empty<LaserPosition>();
        private LaserPosition[] _lastHitFollowPath = Array.Empty<LaserPosition>();
        private (HitRecord Record, List<LaserPosition> Path, double OffsetX, double OffsetY,
            TargetPreviewModel Calibration)? _recordingFollowPath;
        private TriggerTypeEnum _trigger = TriggerTypeEnum.FirstSpot;
        private bool _spotWasVisible;
        private long? _lastAcceptedExternalTriggerEventTimestamp;
        private long? _externalPressedAt;
        private long _externalReleasedAt;
        private long? _sessionStartedAt;
        private int _startCountdown;
        private TimeSpan? _sessionEndedAt;
        private SessionEndReason? _sessionEndReason;
        private TimeSpan? _sessionTimeLimit;
        private int? _remainingHits;
        private int _monoscopeHitCount;
        private CancellationTokenSource? _sessionTimeout;

        private TimeSpan NextDetectionTimestamp { get; set; }

        internal event Action? SessionStatusChanged;
        internal event Action<bool>? SessionRunningChanged;
        internal event Action<HitPosition, TargetModel>? HitRecorded;
        internal event Action? MonoscopeHitCountChanged;
        internal event Action<Position?>? MonoscopeHitEvaluated;

        internal bool IsMonoscope => calibration.Settings.IsMonoscope;
        internal int MonoscopeHitCount => Volatile.Read(ref _monoscopeHitCount);

        internal (bool CanStart, bool IsRunning, TimeSpan Elapsed, TimeSpan? TimeLimit, int StartCountdown,
            SessionEndReason? EndReason, double LastHitCaliberMm) GetSessionStatus()
        {
            lock (_hitGate)
            {
                var elapsed = _sessionEndedAt ?? (_sessionStartedAt is { } startedAt
                    ? Stopwatch.GetElapsedTime(startedAt)
                    : TimeSpan.Zero);
                if (_sessionTimeLimit is { } limit && elapsed > limit)
                    elapsed = limit;
                return (_startCountdown == 0 && _sessionStartedAt is null,
                    _sessionStartedAt.HasValue && !_sessionEndedAt.HasValue,
                    elapsed, _sessionTimeLimit, _startCountdown, _sessionEndReason, _lastHitCaliberDiameterMm);
            }
        }

        internal void StartSession()
        {
            lock (_hitGate)
            {
                if (_startCountdown > 0 || _sessionStartedAt.HasValue)
                    return;

                Reset();
                if (IsMonoscope)
                {
                    _sessionStartedAt = Stopwatch.GetTimestamp();
                    SessionRunningChanged?.Invoke(true);
                    return;
                }

                _remainingHits = SessionLimits.IsHitLimitEnabled ? SessionLimits.HitCountInitial : null;
                _sessionTimeLimit = SessionLimits.IsTimeLimitEnabled
                    ? TimeSpan.FromSeconds(SessionLimits.TimeLimitSecondsInitial)
                    : null;

                _startCountdown = 3;
                _sessionTimeout = new CancellationTokenSource();
                _ = RunStartCountdownAsync(_sessionTimeout.Token);
            }
        }

        private async Task RunStartCountdownAsync(CancellationToken cancellationToken)
        {
            try
            {
                for (int remaining = 2; remaining >= 0; remaining--)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    lock (_hitGate)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        _startCountdown = remaining;
                        if (remaining == 0)
                        {
                            long startedAt = Stopwatch.GetTimestamp();
                            _sessionStartedAt = startedAt;
                            SessionRunningChanged?.Invoke(true);
                            if (_remainingHits == 0)
                            {
                                EndSession(TimeSpan.Zero, SessionEndReason.HitLimit);
                                return;
                            }
                            if (_sessionTimeLimit is { } limit)
                                _ = WaitForSessionLimitAsync(startedAt, limit, cancellationToken);
                        }
                        SessionStatusChanged?.Invoke();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task WaitForSessionLimitAsync(long startedAt, TimeSpan limit, CancellationToken cancellationToken)
        {
            try
            {
                TimeSpan remaining;
                while ((remaining = limit - Stopwatch.GetElapsedTime(startedAt)) > TimeSpan.Zero)
                {
                    // Bound the delay for limits longer than the timer API supports.
                    var delay = TimeSpan.FromMilliseconds(Math.Clamp(remaining.TotalMilliseconds, 1, 86_400_000));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                lock (_hitGate)
                {
                    if (!cancellationToken.IsCancellationRequested && _sessionStartedAt == startedAt)
                        EndSession(limit, SessionEndReason.TimeLimit);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private bool CanDetectHits()
        {
            if (_startCountdown > 0 || _sessionEndedAt.HasValue)
                return false;
            if (_sessionTimeLimit is { } limit && _sessionStartedAt is { } startedAt &&
                Stopwatch.GetElapsedTime(startedAt) >= limit)
            {
                EndSession(limit, SessionEndReason.TimeLimit);
                return false;
            }
            return true;
        }

        private void EndSession(TimeSpan elapsed, SessionEndReason reason)
        {
            _sessionEndedAt = elapsed;
            _sessionEndReason = reason;
            StopSessionTimeout();
            CompleteRecordingFollowPath();
            SessionRunningChanged?.Invoke(false);
            SessionStatusChanged?.Invoke();
        }

        private void StopSessionTimeout()
        {
            _sessionTimeout?.Cancel();
            _sessionTimeout?.Dispose();
            _sessionTimeout = null;
        }

        // Raised on the capture thread. The target point is present only with a valid calibration.
        internal event Action<LaserPosition?, Position?, TimeSpan>? LaserPositionEvaluated;
        internal event Action<double, Position?, double>? GuidanceAimEvaluated;

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
                    CompleteRecordingFollowPath();
                    aimPositionHandler.ClearFollowPath();
                }
            }
        }

        internal void OnExternalTriggerChanged(bool isPressed, long timestamp)
        {
            lock (_hitGate)
            {
                if (_startCountdown > 0 || _sessionEndedAt.HasValue ||
                    (_sessionStartedAt is { } startedAt && timestamp < startedAt))
                    return;

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
                ClearShotState();
        }

        internal (int X, int Y)? GetOffsetToCenterLastHit()
        {
            lock (_hitGate)
            {
                if (_lastHit is not { } hit || calibration.GetCameraTargetCenterPixels() is not { } center)
                    return null;

                return (ClampOffset(center.X - hit.X), ClampOffset(center.Y - hit.Y));
            }

            static int ClampOffset(double offset) =>
                (int)Math.Clamp(Math.Round(offset), WeaponHandler.MinimumOffset, WeaponHandler.MaximumOffset);
        }

        internal void Reset()
        {
            lock (_hitGate)
            {
                bool wasRunning = _sessionStartedAt.HasValue && !_sessionEndedAt.HasValue;
                StopSessionTimeout();
                _startCountdown = 0;
                _sessionStartedAt = null;
                _sessionEndedAt = null;
                _sessionEndReason = null;
                _sessionTimeLimit = null;
                _remainingHits = null;
                _monoscopeHitCount = 0;
                _spotWasVisible = false;
                _externalPressedAt = null;
                _lastAcceptedExternalTriggerEventTimestamp = null;
                NextDetectionTimestamp = TimeSpan.Zero;
                ClearShotState();
                recording.ClearNormalizedHitRecordList();
                if (wasRunning)
                    SessionRunningChanged?.Invoke(false);
            }
        }

        private void ClearShotState()
        {
            aimPositionHandler.ClearAimPaths();
            _lastHit = null;
            _lastTargetHit = null;
            _lastHitAimPath = Array.Empty<LaserPosition>();
            _lastHitFollowPath = Array.Empty<LaserPosition>();
            CompleteRecordingFollowPath();
        }

        private void CompleteRecordingFollowPath()
        {
            _recordingFollowPath?.Record.CompleteAimPathAfter();
            _recordingFollowPath = null;
        }

        internal (AimPosition? Hit, LaserPosition[] AimPath, LaserPosition[] FollowPath,
            Position? LastTargetHit, double CaliberDiameterMm, AimPosition? CurrentAim) ProcessFrame(
            Mat frame, long frameCapturedTimestampMs, double detectedFps, CancellationToken cancellationToken)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(appStartedTimestampMs, frameCapturedTimestampMs);
            LaserPosition? registeredHit = null;
            LaserPosition? lastHit;
            LaserPosition? position;
            LaserPosition[] aimPath;
            LaserPosition[] followPath;
            Position? targetPoint = null;
            Position? lastTargetHit;
            double caliberDiameterMm;
            lock (_hitGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reportGuidance = GuidanceAimEvaluated;
                if (!CanDetectHits() || (_sessionStartedAt is { } startedAt && frameCapturedTimestampMs < startedAt))
                {
                    reportGuidance?.Invoke(double.NaN, null, 0);
                    return GetLastHitSnapshot();
                }

                bool isMonoscope = IsMonoscope;
                if (isMonoscope && !_sessionStartedAt.HasValue)
                {
                    reportGuidance?.Invoke(double.NaN, null, 0);
                    return GetLastHitSnapshot();
                }

                position = _laserDetector.GetLaserPosition(frame, elapsed);
                if (reportGuidance is not null)
                {
                    if (!isMonoscope && _trigger is (TriggerTypeEnum.TimeOut or TriggerTypeEnum.ExternalEvent)
                        && position is { } laser)
                    {
                        var aim = aimPositionHandler.GetAimPosition(laser);
                        if (calibration.MapHit(aim.X / frame.Width, aim.Y / frame.Height, out var target) is { } mapped
                            && target.MaxDiameterMm is > 0)
                        {
                            double offsetX = mapped.X - TargetModel.Center.X;
                            double offsetY = mapped.Y - TargetModel.Center.Y;
                            // Target coordinates are normalized by the outer ring diameter.
                            double outsideDistanceInTargetRadii = Math.Max(0, 2 * double.Hypot(offsetX, offsetY) - 1);
                            reportGuidance(target.CalculateScore(mapped), mapped, outsideDistanceInTargetRadii);
                        }
                        else
                        {
                            reportGuidance(double.NaN, null, 0);
                        }
                    }
                    else
                    {
                        reportGuidance(double.NaN, null, 0);
                    }
                }
                bool startFollowPath = false;
                HitRecord? hitRecord = null;
                TargetPreviewModel? calibrationAtHit = null;
                if (_trigger == TriggerTypeEnum.TimeOut && position is null &&
                    aimPositionHandler.GetTimedOutAimPosition(elapsed) is { } timedOutPosition)
                {
                    if (aimPositionHandler.AimPathDuration >= _aimPathTimeoutMinimumDuration &&
                        (isMonoscope || IsAimPositionInsideAllowedZone(aimPositionHandler.GetAimPosition(timedOutPosition), frame)))
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

                if (registeredHit is not null && !CanDetectHits())
                    return GetLastHitSnapshot();

                if (isMonoscope)
                {
                    if (registeredHit is { } monoscopeHit)
                        ProcessMonoscopeHit(monoscopeHit, frame.Width, frame.Height);
                    return (null, [], [], _lastTargetHit, 0,
                        position is { } current ? aimPositionHandler.GetAimPosition(current) : null);
                }

                if (registeredHit is { } registered)
                {
                    _lastHit = registeredHit;
                    _lastHitCaliberDiameterMm = (double)weaponHandler.CaliberDiameterMm;
                    var aim = aimPositionHandler.GetAimPosition(registered);
                    targetPoint = calibration.MapHit(aim.X / frame.Width, aim.Y / frame.Height, out calibrationAtHit);
                    _lastTargetHit = targetPoint;
                    double offsetX = aim.X - registered.X;
                    double offsetY = aim.Y - registered.Y;
                    if (targetPoint is { } mapped)
                    {
                        var recordedAimPath = MapAimPath(_lastHitAimPath, offsetX, offsetY, frame.Width, frame.Height);
                        var score = calibrationAtHit.CalculateScore(mapped, _lastHitCaliberDiameterMm);
						var hitPosition = new HitPosition(mapped.X, mapped.Y, registered.Time, score);
                        hitRecord = new HitRecord(hitPosition,
                            recordedAimPath, calibrationAtHit.GetDistanceFromCenterMm(mapped));
                    }

                    CompleteRecordingFollowPath();
                    if (startFollowPath && hitRecord is not null)
                        _recordingFollowPath = (hitRecord, hitRecord.BeginAimPathAfter(), offsetX, offsetY, calibrationAtHit);
                }

                lastHit = _lastHit;
                aimPath = _lastHitAimPath;
                var (updatedFollowPath, addedPoint, isCollecting) =
                    aimPositionHandler.UpdateFollowPath(position, elapsed, detectedFps, startFollowPath);
                if (startFollowPath || updatedFollowPath.Length > 0)
                    _lastHitFollowPath = updatedFollowPath;

                if (_recordingFollowPath is { } active && !ReferenceEquals(active.Calibration, calibration.Settings))
                    CompleteRecordingFollowPath();

                if (_recordingFollowPath is { } pending && updatedFollowPath.Length > 0)
                {
                    var oldestTime = updatedFollowPath[0].Time;
                    int removedCount = 0;
                    while (removedCount < pending.Path.Count && pending.Path[removedCount].Time < oldestTime)
                        removedCount++;
                    pending.Path.RemoveRange(0, removedCount);

                    if (addedPoint is not null &&
                        MapAimPoint(addedPoint, pending.OffsetX, pending.OffsetY, frame.Width, frame.Height) is { } mapped &&
                        ReferenceEquals(pending.Calibration, calibration.Settings))
                        pending.Path.Add(mapped);
                }

                if (!isCollecting)
                    CompleteRecordingFollowPath();

                if (hitRecord is not null && calibrationAtHit is not null &&
                    recording.TryAddNormalizedHitRecord(hitRecord, calibrationAtHit))
                {
                    HitRecorded?.Invoke(hitRecord.HitPosition, calibrationAtHit);
                    if (_remainingHits is { } remainingHits && _sessionStartedAt is { } sessionStart)
                    {
                        _remainingHits = remainingHits - 1;
                        if (_remainingHits == 0)
                        {
                            var sessionElapsed = Stopwatch.GetElapsedTime(sessionStart);
                            EndSession(_sessionTimeLimit is { } limit && sessionElapsed > limit ? limit : sessionElapsed,
                                SessionEndReason.HitLimit);
                        }
                    }
                }

                followPath = _lastHitFollowPath;
                lastTargetHit = _lastTargetHit;
                caliberDiameterMm = _lastHitCaliberDiameterMm;
            }

            LaserPositionEvaluated?.Invoke(registeredHit, targetPoint, elapsed);

            if (registeredHit is { } hit)
                Console.WriteLine($"Laser detected at ({hit.X}, {hit.Y})");

            return (lastHit is { } lastPosition ? aimPositionHandler.GetAimPosition(lastPosition) : null,
                aimPath, followPath, lastTargetHit, caliberDiameterMm,
                position is { } currentPosition ? aimPositionHandler.GetAimPosition(currentPosition) : null);
        }

        private void ProcessMonoscopeHit(LaserPosition hit, int frameWidth, int frameHeight)
        {
            if (calibration.MapHit(hit.X / frameWidth, hit.Y / frameHeight, out var target) is not { } point ||
                !target.IsMonoscope)
                return;

            // The perspective matrix uses float input points, so allow rounding at the edges.
            const double tolerance = 1e-7;
            Position? destinationPixel = null;
            if (point.X is >= -tolerance and <= 1 + tolerance &&
                point.Y is >= -tolerance and <= 1 + tolerance)
            {
                double x = Math.Clamp(point.X, 0, 1);
                double y = Math.Clamp(point.Y, 0, 1);
                _lastTargetHit = new Position(x, y);
                destinationPixel = new Position(x * (target.Width - 1), y * (target.Height - 1));
            }

            _monoscopeHitCount++;
            MonoscopeHitCountChanged?.Invoke();
            MonoscopeHitEvaluated?.Invoke(destinationPixel);
        }

        private (AimPosition? Hit, LaserPosition[] AimPath, LaserPosition[] FollowPath,
            Position? LastTargetHit, double CaliberDiameterMm, AimPosition? CurrentAim) GetLastHitSnapshot() =>
            (_lastHit is { } hit ? aimPositionHandler.GetAimPosition(hit) : null,
                _lastHitAimPath, _lastHitFollowPath, _lastTargetHit, _lastHitCaliberDiameterMm, null);

        private IReadOnlyList<LaserPosition> MapAimPath(LaserPosition[] path, double offsetX, double offsetY,
            int frameWidth, int frameHeight)
        {
            if (path.Length == 0)
                return Array.Empty<LaserPosition>();

            var mappedPath = new List<LaserPosition>(path.Length);
            foreach (var point in path)
            {
                if (MapAimPoint(point, offsetX, offsetY, frameWidth, frameHeight) is { } mapped)
                    mappedPath.Add(mapped);
            }

            return mappedPath.AsReadOnly();
        }

        private LaserPosition? MapAimPoint(LaserPosition point, double offsetX, double offsetY,
            int frameWidth, int frameHeight) =>
            calibration.MapHit((point.X + offsetX) / frameWidth, (point.Y + offsetY) / frameHeight) is { } mapped
                ? new LaserPosition(mapped.X, mapped.Y, point.Time)
                : null;

        private bool IsAimPositionInsideAllowedZone(AimPosition position, Mat frame)
        {
            var allowedArea = AllowedAreaModel.GetBorders(frame, detectionSettings);
            return position.X >= allowedArea.Left && position.X < allowedArea.Right &&
                position.Y >= allowedArea.Top && position.Y < allowedArea.Bottom;
        }

        private bool IsTriggered(LaserPosition? position, long capturedAt, bool intervalElapsed)
        {
            if (_trigger == TriggerTypeEnum.ExternalEvent)
            {
                // Compare against capture time, even if the frame is processed after key-up.
                return position is not null && _externalPressedAt is { } pressedAt &&
                    capturedAt >= pressedAt && capturedAt < _externalReleasedAt;
            }

            if (_trigger == TriggerTypeEnum.FirstSpot)
                return position is not null && intervalElapsed;

            // Observe every frame so a spot that appeared during the interval is not a new spot afterward.
            bool appeared = position is not null && !_spotWasVisible;
            _spotWasVisible = position is not null;
            return appeared && intervalElapsed;
        }
    }
}
