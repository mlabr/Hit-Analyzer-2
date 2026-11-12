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
        private readonly List<LaserPosition> _followPath = new();
        private readonly TimeSpan _pathTimeout = TimeSpan.FromMilliseconds(settings.AimPathTimeoutMs);
        private readonly TimeSpan _followDuration = TimeSpan.FromSeconds(settings.AimPathAfterTimeLimit);
        private TimeSpan _followEndsAt;
        private (double X, double Y) _offset;

        // Path operations are serialized by the shooting session.
        internal void UpdateAimPath(LaserPosition? position, TimeSpan elapsed, double detectedFps)
        {
            if (HasExpired(AimPositionList, elapsed))
                AimPositionList.Clear();

            AppendPosition(AimPositionList, position, settings.AimPathTimeLimit * detectedFps);
        }

        internal LaserPosition[] UpdateFollowPath(LaserPosition? position, TimeSpan elapsed, double detectedFps,
            bool externalHit)
        {
            if (externalHit)
            {
                _followPath.Clear();
                _followEndsAt = elapsed + _followDuration;
            }
            else if (HasExpired(_followPath, elapsed))
            {
                _followPath.Clear();
            }

            // Once the last point expires, a new external hit is needed to start tracking again.
            if ((externalHit || _followPath.Count > 0) && elapsed <= _followEndsAt)
                AppendPosition(_followPath, position, settings.AimPathAfterTimeLimit * detectedFps);

            return _followPath.ToArray();
        }

        private bool HasExpired(List<LaserPosition> path, TimeSpan elapsed) =>
            path.Count > 0 && elapsed - path[^1].Time > _pathTimeout;

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

        internal void ClearAimPaths()
        {
            AimPositionList.Clear();
            ClearFollowPath();
        }

        internal void ClearFollowPath() => _followPath.Clear();

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
