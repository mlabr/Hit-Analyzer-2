using System;
using HitAnalyzer2.Services.Detection;

namespace HitAnalyzer2.Services.Shooting
{
    internal readonly record struct AimPosition(double X, double Y, TimeSpan Time);

    internal sealed class AimHandler
    {
        private readonly object _offsetGate = new();
        private (double X, double Y) _offset;

        internal void SetOffset(double x, double y)
        {
            lock (_offsetGate)
                _offset = (x, y);
        }

        internal AimPosition GetAimPosition(LaserPosition position)
        {
            lock (_offsetGate)
                return new AimPosition(position.X + _offset.X, position.Y + _offset.Y, position.Time);
        }
    }
}
