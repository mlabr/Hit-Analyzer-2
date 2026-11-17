using System;

namespace HitAnalyzer2.Models
{
    internal sealed record CalibrationCorners(
        Position TopLeft, Position TopRight,
        Position BottomRight, Position BottomLeft)
    {
        internal static CalibrationCorners CenteredSquare(double width, double height)
        {
            double halfSide = 0.4 * Math.Min(width, height);
            double x = halfSide / width;
            double y = halfSide / height;
            return new(new(0.5 - x, 0.5 - y), new(0.5 + x, 0.5 - y),
                new(0.5 + x, 0.5 + y), new(0.5 - x, 0.5 + y));
        }
    }
}
