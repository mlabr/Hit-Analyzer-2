using HitAnalyzer2.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HitAnalyzer2.Models
{
    internal record TargetModel
    {
        internal static readonly Position Center = new(0.5, 0.5);

        internal string? TargetName { get; init; }
        internal TargetTypeEnum TargetType { get; init; }
        internal bool IsMonoscope => TargetType == TargetTypeEnum.Monoscope;
        internal int Width { get; init; }
        internal int Height { get; init; }
        internal IReadOnlyList<TargetRingModel> Rings { get; init; } = [];
        internal double? MarkedRingPoints { get; init; }
        internal double? MaxDiameter => Rings.Count == 0 ? null : Rings.Max(ring => ring.Diameter);
        internal double MillimetersPerUnit => TargetType == TargetTypeEnum.CircleImperial ? 25.4 : 1;
        internal double? MaxDiameterMm => MaxDiameter * MillimetersPerUnit;

        internal double? GetDistanceFromCenterMm(Position hitPosition) =>
            MaxDiameterMm is > 0 and < double.PositiveInfinity and var diameterMm
                ? double.Hypot(hitPosition.X - Center.X, hitPosition.Y - Center.Y) * diameterMm
                : null;

        internal double CalculateScore(Position hitPosition, double caliberDiameterMm = 0)
        {
            if (GetDistanceFromCenterMm(hitPosition) is not { } distanceMm ||
                !double.IsFinite(distanceMm) || !double.IsFinite(caliberDiameterMm) || caliberDiameterMm < 0)
                return 0;

            double projectileRadiusMm = caliberDiameterMm / 2;
            foreach (var ring in Rings.OrderBy(ring => ring.Diameter))
            {
                double ringRadiusMm = ring.Radius * MillimetersPerUnit;
                if (distanceMm <= ringRadiusMm + projectileRadiusMm)
                    return ring.PointValue;
            }

            return 0;
        }
    }
}
