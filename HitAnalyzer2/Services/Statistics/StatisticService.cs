using HitAnalyzer2.Models;
using HitAnalyzer2.Models.Shooting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HitAnalyzer2.Services.Statistics
{
    internal sealed record GroupStatistic(HitGroupCircle Circle, double? DiameterMm);
    internal sealed record MeanPointOfImpact(double DistanceMm, double XMm, double YMm);
    internal sealed record ScoreHistogram(double Score, int Count);
    internal sealed record SessionStatistics(GroupStatistic? Cep50, GroupStatistic? Cep80,
        MeanPointOfImpact? MeanPointOfImpact, IReadOnlyList<ScoreHistogram> ScoreHistogram);

    internal static class StatisticService
    {
        internal static SessionStatistics Calculate(IReadOnlyList<HitRecord> hits, TargetModel target)
        {
            var histogram = BuildScoreHistogram(hits, target);
            if (hits.Count == 0)
                return new(null, null, null, histogram);

            var (cep50, cep80) = HitGroup.AroundMean(GetHitPoints(hits));
            return new(DescribeCircle(cep50, target), DescribeCircle(cep80, target),
                CalculateMeanPointOfImpact(cep80.Center, target), histogram);
        }

        internal static Task<GroupStatistic> CalculateEnhancedR80Async(IReadOnlyList<HitRecord> hits,
            TargetModel target, GroupStatistic cep80, CancellationToken cancellationToken) =>
            Task.Run(() =>
            {
                var circle = HitGroup.SmallestCircle(GetHitPoints(hits), cep80.Circle, cancellationToken);
                return DescribeCircle(circle, target);
            }, cancellationToken);

        private static Position[] GetHitPoints(IReadOnlyList<HitRecord> hits) =>
            hits.Select(hit => (Position)hit.HitPosition).ToArray();

        private static GroupStatistic DescribeCircle(HitGroupCircle circle, TargetModel target) =>
            new(circle, target.MaxDiameterMm is > 0 and < double.PositiveInfinity and var diameterMm
                ? 2 * circle.Radius * diameterMm
                : null);

        private static MeanPointOfImpact? CalculateMeanPointOfImpact(Position mean, TargetModel target)
        {
            double? diameterMm = target.MaxDiameterMm;
            if (diameterMm is not (> 0 and < double.PositiveInfinity) ||
                target.GetDistanceFromCenterMm(mean) is not { } distanceMm || !double.IsFinite(distanceMm))
                return null;

            // Match the offset controls: positive X is right and positive Y is up.
            return new(distanceMm, (mean.X - TargetModel.Center.X) * diameterMm.Value,
                (TargetModel.Center.Y - mean.Y) * diameterMm.Value);
        }

        private static IReadOnlyList<ScoreHistogram> BuildScoreHistogram(IReadOnlyList<HitRecord> hits, TargetModel target)
        {
            var counts = hits.GroupBy(hit => hit.HitPosition.Score)
                .ToDictionary(group => group.Key, group => group.Count());
            counts.TryAdd(0, 0);
            foreach (var ring in target.Rings)
                counts.TryAdd(ring.PointValue, 0);

            return counts.OrderBy(bin => bin.Key).Select(bin => new ScoreHistogram(bin.Key, bin.Value)).ToArray();
        }
    }
}
