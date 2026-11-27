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
        private readonly List<LaserPosition> AimPositionList = new();
        private readonly List<LaserPosition> _aimPositionAfterList = new();
        private readonly TimeSpan _aimPathTimeLimit = TimeSpan.FromSeconds(settings.AimPathTimeLimit);
        private readonly TimeSpan _aimPathTimeoutMs = TimeSpan.FromMilliseconds(settings.AimPathTimeoutMs);
        private readonly TimeSpan _aimPathAfterTimeLimit = TimeSpan.FromSeconds(settings.AimPathAfterTimeLimit);
        private TimeSpan _followEndsAt;

        internal LaserPosition? LastAimPosition => AimPositionList.Count > 0 ? AimPositionList[^1] : null;

        internal TimeSpan AimPathDuration => AimPositionList.Count > 1
            ? AimPositionList[^1].Time - AimPositionList[0].Time
            : TimeSpan.Zero;

        internal LaserPosition? GetTimedOutAimPosition(TimeSpan elapsed) =>
            LastAimPosition is { } lastPosition && elapsed - lastPosition.Time >= _aimPathTimeoutMs
                ? lastPosition
                : null;

        // Path operations are serialized by the shooting session.
        internal void UpdateAimPath(LaserPosition? position, TimeSpan elapsed)
        {
            if (HasExpired(AimPositionList, elapsed))
                ClearAimPath();

            var cutoff = elapsed - _aimPathTimeLimit;
            int expiredCount = 0;
            while (expiredCount < AimPositionList.Count && AimPositionList[expiredCount].Time < cutoff)
                expiredCount++;
            if (expiredCount > 0)
                AimPositionList.RemoveRange(0, expiredCount);

            if (position is not null && _aimPathTimeLimit > TimeSpan.Zero)
                AimPositionList.Add(position);
        }

        internal (LaserPosition[] Path, LaserPosition? AddedPoint, bool IsCollecting) UpdateFollowPath(
            LaserPosition? position, TimeSpan elapsed,
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
            LaserPosition? addedPoint = null;
            if ((externalHit || _aimPositionAfterList.Count > 0) && elapsed <= _followEndsAt &&
                position is not null && _aimPathAfterTimeLimit > TimeSpan.Zero)
            {
                _aimPositionAfterList.Add(position);
                addedPoint = position;
            }

            bool isCollecting = _aimPositionAfterList.Count > 0 && elapsed <= _followEndsAt;
            return (_aimPositionAfterList.ToArray(), addedPoint, isCollecting);
        }

        private bool HasExpired(List<LaserPosition> path, TimeSpan elapsed) =>
            path.Count > 0 && elapsed - path[^1].Time > _aimPathTimeoutMs;

        internal LaserPosition[] GetAimPathSnapshot() => AimPositionList.ToArray();

        internal void ClearAimPath() => AimPositionList.Clear();

        internal void ClearAimPaths()
        {
            ClearAimPath();
            ClearFollowPath();
        }

        internal void ClearFollowPath() => _aimPositionAfterList.Clear();

    }
}
