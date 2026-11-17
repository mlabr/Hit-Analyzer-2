using HitAnalyzer2.Models;
using System;

namespace HitAnalyzer2
{
    internal class LaserPosition : Position
    {
        public LaserPosition(double x, double y, TimeSpan time) : base(x, y)
        {
            Time = time;
        }

        public TimeSpan Time { get; }
    }
}
