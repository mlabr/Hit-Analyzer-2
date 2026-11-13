using System;

namespace HitAnalyzer2
{
    internal readonly record struct LaserPosition(double X, double Y, TimeSpan Time)
    {
        public double X { get; } = X;
        public double Y { get; } = Y;
        public TimeSpan Time { get; } = Time;
    }
}
