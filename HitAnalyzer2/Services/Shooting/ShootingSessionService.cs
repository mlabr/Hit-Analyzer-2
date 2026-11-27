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
    public enum HitDetectionReadiness { Waiting, Tracking, Ready }

    internal sealed class ShootingSessionService(ILaserDetector laserDetector, AimPositionHandler aimPositionHandler,
        WeaponHandler weaponHandler, ShootingSettings shootingSettings, DetectionSettings detectionSettings, TargetCalibrationService calibration,
        RecordingService recording, long appStartedTimestampMs)
    {
        internal enum SessionEndReason { TimeLimit, HitLimit }
        internal sealed record SessionInfo(string Shooter, string? Weapon, decimal RealRange, decimal SimulatedRange);
        internal sealed record CompletedSession(SessionInfo Info, DateTimeOffset StartedAt, TimeSpan HitTimeOrigin,
            TimeSpan Elapsed, TimeSpan? TimeLimit, int? HitLimit, SessionEndReason EndReason,
            double CaliberMm, TargetModel Target, IReadOnlyList<HitRecord> Hits);

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
        private LaserPosition? _lastHit;
        private double _lastHitCaliberDiameterMm;
        private Position? _lastTargetHit;
        private LaserPosition[] _lastHitAimPath = Array.Empty<LaserPosition>();
        private LaserPosition[] _lastHitFollowPath = Array.Empty<LaserPosition>();
        private (HitRecord Record, List<LaserPosition> Path, double OffsetX, double OffsetY,
            TargetPreviewModel Calibration)? _recordingFollowPath;
        private bool _spotWasVisible;
        private long? _lastAcceptedExternalTriggerEventTimestamp;
        private long? _externalPressedAt;
        private long _externalReleasedAt;
        private long? _sessionStartedAt;
        private DateTimeOffset _sessionStartedAtLocal;
        private SessionInfo? _sessionInfo;
        private int? _sessionHitLimit;
        private int _startCountdown;
        private TimeSpan? _sessionEndedAt;
        private SessionEndReason? _sessionEndReason;
        private TimeSpan? _sessionTimeLimit;
        private int? _remainingHits;
        private int _monoscopeHitCount;
        private CancellationTokenSource? _sessionTimeout;

        private TimeSpan NextDetectionTimestamp { get; set; }

        internal event Action? SessionStatusChanged;
        internal event Action<CompletedSession>? SessionCompleted;
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

        internal void StartSession(string shooter, decimal realRange, decimal simulatedRange)
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

                _sessionInfo = new(shooter, weaponHandler.Current.Name, realRange, simulatedRange);
                _sessionHitLimit = SessionLimits.IsHitLimitEnabled ? SessionLimits.HitCountInitial : null;
                _remainingHits = _sessionHitLimit;
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
                            _sessionStartedAtLocal = DateTimeOffset.Now;
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
            if (_sessionInfo is { } info && _sessionStartedAt is { } startedAt)
            {
                var (target, hits) = recording.GetSnapshot();
                double caliber = hits.Count == 0 ? (double)weaponHandler.Current.CaliberDiameterMm : _lastHitCaliberDiameterMm;
                SessionCompleted?.Invoke(new(info, _sessionStartedAtLocal,
                    Stopwatch.GetElapsedTime(appStartedTimestampMs, startedAt), elapsed,
                    _sessionTimeLimit, _sessionHitLimit, reason, caliber, target, hits));
            }
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
                var previous = weaponHandler.Current.Settings.Trigger;
                if (previous == trigger)
                    return;

                weaponHandler.SetTrigger(trigger);
                ResetTriggerState(previous, trigger);
            }
        }

        internal void SelectWeapon(string? fileName, WeaponProfileModel? profile)
        {
            lock (_hitGate)
            {
                var previous = weaponHandler.Current.Settings.Trigger;
                weaponHandler.Select(fileName, profile);
                var trigger = weaponHandler.Current.Settings.Trigger;
                if (previous != trigger)
                    ResetTriggerState(previous, trigger);
            }
        }

        private void ResetTriggerState(TriggerTypeEnum previous, TriggerTypeEnum trigger)
        {
            bool resetTimeoutPath = previous == TriggerTypeEnum.TimeOut || trigger == TriggerTypeEnum.TimeOut;
            if (resetTimeoutPath)
                aimPositionHandler.ClearAimPaths();

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

        internal void OnExternalTriggerChanged(bool isPressed, long timestamp)
        {
            lock (_hitGate)
            {
                if (_startCountdown > 0 || _sessionEndedAt.HasValue ||
                    (_sessionStartedAt is { } startedAt && timestamp < startedAt))
                    return;

                if (isPressed)
                {
                    var settings = weaponHandler.Current.Settings;
                    if (settings.Trigger == TriggerTypeEnum.ExternalEvent &&
                        (_lastAcceptedExternalTriggerEventTimestamp is not { } previousPress ||
                         Stopwatch.GetElapsedTime(previousPress, timestamp) >=
                         TimeSpan.FromMilliseconds(settings.NexDetectionInterval)))
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
                _sessionInfo = null;
                _sessionHitLimit = null;
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
            Position? LastTargetHit, double CaliberDiameterMm, AimPosition? CurrentAim, HitDetectionReadiness Readiness, double WeaponCaliberMm) ProcessFrame(
            Mat frame, long frameCapturedTimestampMs, CancellationToken cancellationToken)
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
            HitDetectionReadiness readiness;
            ActiveWeapon weapon;
            lock (_hitGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                weapon = weaponHandler.Current;
                var settings = weapon.Settings;
                var reportGuidance = GuidanceAimEvaluated;
                if (!CanDetectHits() || (_sessionStartedAt is { } startedAt && frameCapturedTimestampMs < startedAt))
                {
                    reportGuidance?.Invoke(double.NaN, null, 0);
                    return GetLastHitSnapshot(weapon);
                }

                bool isMonoscope = IsMonoscope;
                if (isMonoscope && !_sessionStartedAt.HasValue)
                {
                    reportGuidance?.Invoke(double.NaN, null, 0);
                    return GetLastHitSnapshot(weapon);
                }

                position = _laserDetector.GetLaserPosition(frame, elapsed);
                if (reportGuidance is not null)
                {
                    if (!isMonoscope && settings.Trigger is (TriggerTypeEnum.TimeOut or TriggerTypeEnum.ExternalEvent)
                        && position is { } laser)
                    {
                        var aim = weapon.GetAimPosition(laser);
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
                if (settings.Trigger == TriggerTypeEnum.TimeOut && position is null &&
                    aimPositionHandler.GetTimedOutAimPosition(elapsed) is { } timedOutPosition)
                {
                    if (IsTimeOutPathReady(timedOutPosition, frame, isMonoscope, weapon))
                    {
                        registeredHit = timedOutPosition;
                        _lastHitAimPath = aimPositionHandler.GetAimPathSnapshot();
                    }

                    aimPositionHandler.ClearAimPath();
                }
                else
                {
                    aimPositionHandler.UpdateAimPath(position, elapsed);
                    if (settings.Trigger != TriggerTypeEnum.TimeOut &&
                        IsTriggered(settings.Trigger, position, frameCapturedTimestampMs, elapsed >= NextDetectionTimestamp))
                    {
                        registeredHit = position;
                        if (settings.Trigger == TriggerTypeEnum.ExternalEvent)
                        {
                            _externalPressedAt = null;
                            _lastHitAimPath = aimPositionHandler.GetAimPathSnapshot();
                            startFollowPath = true;
                        }
                        else
                        {
                            NextDetectionTimestamp = elapsed + TimeSpan.FromMilliseconds(settings.NexDetectionInterval);
                        }
                    }
                }

                if (registeredHit is not null && !CanDetectHits())
                    return GetLastHitSnapshot(weapon);

                if (isMonoscope)
                {
                    if (registeredHit is { } monoscopeHit)
                        ProcessMonoscopeHit(monoscopeHit, frame.Width, frame.Height);
                    return (null, [], [], _lastTargetHit, 0,
                        position is { } current ? weapon.GetAimPosition(current) : null,
                        GetDetectionReadiness(frame, elapsed, frameCapturedTimestampMs, isMonoscope, position, weapon), (double)weapon.CaliberDiameterMm);
                }

                if (registeredHit is { } registered)
                {
                    _lastHit = registeredHit;
                    _lastHitCaliberDiameterMm = (double)weapon.CaliberDiameterMm;
                    var aim = weapon.GetAimPosition(registered);
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
                    aimPositionHandler.UpdateFollowPath(position, elapsed, startFollowPath);
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
                readiness = GetDetectionReadiness(frame, elapsed, frameCapturedTimestampMs, isMonoscope, position, weapon);
            }

            LaserPositionEvaluated?.Invoke(registeredHit, targetPoint, elapsed);

            if (registeredHit is { } hit)
                Console.WriteLine($"Laser detected at ({hit.X}, {hit.Y})");

            return (lastHit is { } lastPosition ? weapon.GetAimPosition(lastPosition) : null,
                aimPath, followPath, lastTargetHit, caliberDiameterMm,
                position is { } currentPosition ? weapon.GetAimPosition(currentPosition) : null, readiness, (double)weapon.CaliberDiameterMm);
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
            Position? LastTargetHit, double CaliberDiameterMm, AimPosition? CurrentAim, HitDetectionReadiness Readiness, double WeaponCaliberMm) GetLastHitSnapshot(ActiveWeapon weapon) =>
            (_lastHit is { } hit ? weapon.GetAimPosition(hit) : null,
                _lastHitAimPath, _lastHitFollowPath, _lastTargetHit, _lastHitCaliberDiameterMm, null, HitDetectionReadiness.Waiting,
                (double)weapon.CaliberDiameterMm);

        private static readonly TimeSpan TrackingIndicatorMinimumDuration = TimeSpan.FromMilliseconds(100);

        private HitDetectionReadiness GetDetectionReadiness(Mat frame, TimeSpan elapsed, long capturedAt,
            bool isMonoscope, LaserPosition? position, ActiveWeapon weapon)
        {
            var trigger = weapon.Settings.Trigger;
            if (_sessionEndedAt.HasValue)
                return HitDetectionReadiness.Waiting;

            if (elapsed >= NextDetectionTimestamp &&
                (trigger == TriggerTypeEnum.FirstSpot || (trigger == TriggerTypeEnum.NewSpot && !_spotWasVisible)))
                return HitDetectionReadiness.Ready;

            if (trigger == TriggerTypeEnum.ExternalEvent)
            {
                if (IsExternalTriggerActive(capturedAt))
                    return HitDetectionReadiness.Ready;

                return position is not null ? HitDetectionReadiness.Tracking : HitDetectionReadiness.Waiting;
            }

            if (trigger == TriggerTypeEnum.TimeOut && aimPositionHandler.LastAimPosition is { } lastAim)
            {
                if (IsTimeOutPathReady(lastAim, frame, isMonoscope, weapon))
                    return HitDetectionReadiness.Ready;

                if (aimPositionHandler.AimPathDuration >= TrackingIndicatorMinimumDuration)
                    return HitDetectionReadiness.Tracking;
            }

            return HitDetectionReadiness.Waiting;
        }

        private bool IsTimeOutPathReady(LaserPosition position, Mat frame, bool isMonoscope, ActiveWeapon weapon) =>
            aimPositionHandler.AimPathDuration >= _aimPathTimeoutMinimumDuration &&
            (isMonoscope || IsAimPositionInsideAllowedZone(weapon.GetAimPosition(position), frame));

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

        private bool IsTriggered(TriggerTypeEnum trigger, LaserPosition? position, long capturedAt, bool intervalElapsed)
        {
            if (trigger == TriggerTypeEnum.ExternalEvent)
            {
                // Compare against capture time, even if the frame is processed after key-up.
                return position is not null && IsExternalTriggerActive(capturedAt);
            }

            if (trigger == TriggerTypeEnum.FirstSpot)
                return position is not null && intervalElapsed;

            // Observe every frame so a spot that appeared during the interval is not a new spot afterward.
            bool appeared = position is not null && !_spotWasVisible;
            _spotWasVisible = position is not null;
            return appeared && intervalElapsed;
        }

        private bool IsExternalTriggerActive(long capturedAt) =>
            _externalPressedAt is { } pressedAt && capturedAt >= pressedAt && capturedAt < _externalReleasedAt;
    }
}
