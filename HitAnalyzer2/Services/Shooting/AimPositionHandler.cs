using System;
using System.Collections.Generic;
using HitAnalyzer2.Services.Detection;

namespace HitAnalyzer2.Services.Shooting
{
    internal readonly record struct AimPosition(double X, double Y, LaserPosition LaserPosition)
    {
        public TimeSpan Time => LaserPosition.Time;
    }

    internal sealed class AimPositionHandler(ShootingSettings settings)
    {
        private readonly object _offsetGate = new();
        private readonly List<LaserPosition> AimPositionList = new();
        private readonly List<LaserPosition> _aimPositionAfterList = new();
        private readonly TimeSpan _aimPathTimeoutMs = TimeSpan.FromMilliseconds(settings.AimPathTimeoutMs);
        private readonly TimeSpan _aimPathAfterTimeLimit = TimeSpan.FromSeconds(settings.AimPathAfterTimeLimit);
        private TimeSpan _followEndsAt;
        private (double X, double Y) _offset;

        internal LaserPosition? LastAimPosition => AimPositionList.Count > 0 ? AimPositionList[^1] : null;

        internal TimeSpan AimPathDuration => AimPositionList.Count > 1
            ? AimPositionList[^1].Time - AimPositionList[0].Time
            : TimeSpan.Zero;

        internal LaserPosition? GetTimedOutAimPosition(TimeSpan elapsed) =>
            LastAimPosition is { } lastPosition && elapsed - lastPosition.Time >= _aimPathTimeoutMs
                ? lastPosition
                : null;

        // Path operations are serialized by the shooting session.
        internal void UpdateAimPath(LaserPosition? position, TimeSpan elapsed, double detectedFps)
        {
            if (HasExpired(AimPositionList, elapsed))
                ClearAimPath();

            AppendPosition(AimPositionList, position, settings.AimPathTimeLimit * detectedFps);
        }

        internal LaserPosition[] UpdateFollowPath(LaserPosition? position, TimeSpan elapsed, double detectedFps,
            bool externalHit)
        {
            if (externalHit)
            {
                ClearFollowPath();
                _followEndsAt = elapsed + _aimPathAfterTimeLimit;
            }
            else if (HasExpired(_aimPositionAfterList, elapsed))
            {
                ClearFollowPath();
            }

            // Once the last point expires, a new external hit is needed to start tracking again.
            if ((externalHit || _aimPositionAfterList.Count > 0) && elapsed <= _followEndsAt)
                AppendPosition(_aimPositionAfterList, position, settings.AimPathAfterTimeLimit * detectedFps);

            return _aimPositionAfterList.ToArray();
        }

        private bool HasExpired(List<LaserPosition> path, TimeSpan elapsed) =>
            path.Count > 0 && elapsed - path[^1].Time > _aimPathTimeoutMs;

        private static void AppendPosition(List<LaserPosition> path, LaserPosition? position, double capacity)
        {
            int maxCount = (int)Math.Clamp(capacity, 0, int.MaxValue);
            int pointsToKeep = position.HasValue && maxCount > 0 ? maxCount - 1 : maxCount;
            if (path.Count > pointsToKeep)
                path.RemoveRange(0, path.Count - pointsToKeep);

            if (position is { } point && maxCount > 0)
                path.Add(point);
        }

        internal LaserPosition[] GetAimPathSnapshot() => AimPositionList.ToArray();

        internal void ClearAimPath() => AimPositionList.Clear();

        internal void ClearAimPaths()
        {
            ClearAimPath();
            ClearFollowPath();
        }

        internal void ClearFollowPath() => _aimPositionAfterList.Clear();

        internal void SetOffset(double x, double y)
        {
            lock (_offsetGate)
                _offset = (x, y);
        }

        internal AimPosition GetAimPosition(LaserPosition position)
        {
            lock (_offsetGate)
                return new AimPosition(position.X + _offset.X, position.Y + _offset.Y, position);
        }
    }
}
