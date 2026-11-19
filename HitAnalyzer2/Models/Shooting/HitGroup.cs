using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HitAnalyzer2.Models.Shooting
{
    internal sealed record HitGroupCircle(Position Center, double RadiusSquared, int CoveredHits)
    {
        internal double Radius => Math.Sqrt(RadiusSquared);
    }

    internal static class HitGroup
    {
        internal static (HitGroupCircle Cep50, HitGroupCircle Cep80) AroundMean(IReadOnlyList<Position> points)
        {
            var mean = new Position(points.Average(point => point.X), points.Average(point => point.Y));
            var distances = points.Select(point => DistanceSquared(mean.X, mean.Y, point)).ToArray();
            Array.Sort(distances);
            return (CreateCircle(points.Count - points.Count / 2), CreateCircle(RequiredHits(points.Count)));

            HitGroupCircle CreateCircle(int required)
            {
                double radiusSquared = distances[required - 1];
                return new(mean, radiusSquared, CountCovered(points, mean.X, mean.Y, radiusSquared));
            }
        }

        internal static HitGroupCircle SmallestCircle(IReadOnlyList<Position> points, HitGroupCircle upperBound,
            CancellationToken cancellationToken)
        {
            var best = upperBound;
            int required = RequiredHits(points.Count);
            // An optimal disk is supported by one point, a diameter pair, or three boundary points.
            for (int i = 0; i < points.Count && best.RadiusSquared > 0; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var a = points[i];
                Consider(a.X, a.Y, 0);
                for (int j = i + 1; j < points.Count; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var b = points[j];
                    double bx = b.X - a.X;
                    double by = b.Y - a.Y;
                    double abSquared = bx * bx + by * by;
                    if (abSquared / 4 >= best.RadiusSquared)
                        continue;

                    Consider(a.X + bx / 2, a.Y + by / 2, abSquared / 4);
                    for (int k = j + 1; k < points.Count; k++)
                    {
                        var c = points[k];
                        double cx = c.X - a.X;
                        double cy = c.Y - a.Y;
                        double acSquared = cx * cx + cy * cy;
                        if (acSquared / 4 >= best.RadiusSquared)
                            continue;
                        double determinant = 2 * (bx * cy - by * cx);
                        if (determinant == 0)
                            continue;

                        double x = (cy * abSquared - by * acSquared) / determinant;
                        double y = (bx * acSquared - cx * abSquared) / determinant;
                        Consider(a.X + x, a.Y + y, x * x + y * y);
                    }
                }
            }
            return best;

            void Consider(double x, double y, double radiusSquared)
            {
                if (radiusSquared >= best.RadiusSquared || !double.IsFinite(radiusSquared))
                    return;

                int covered = CountCovered(points, x, y, radiusSquared, required);
                if (covered >= required)
                    best = new(new Position(x, y), radiusSquared, covered);
            }
        }

        private static int RequiredHits(int count) => count - count / 5;

        private static int CountCovered(IReadOnlyList<Position> points, double x, double y,
            double radiusSquared, int required = 0)
        {
            int covered = 0;
            double boundary = radiusSquared * (1 + 1e-12);
            for (int i = 0; i < points.Count; i++)
            {
                if (DistanceSquared(x, y, points[i]) <= boundary)
                    covered++;
                else if (covered + points.Count - i - 1 < required)
                    break;
            }
            return covered;
        }

        private static double DistanceSquared(double x, double y, Position point)
        {
            double dx = x - point.X;
            double dy = y - point.Y;
            return dx * dx + dy * dy;
        }
    }
}
