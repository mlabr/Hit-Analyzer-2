using HitAnalyzer2.Models;
using System;

namespace HitAnalyzer2
{
    internal sealed record LaserPosition(double X, double Y, TimeSpan Time) : Position(X, Y);
}
