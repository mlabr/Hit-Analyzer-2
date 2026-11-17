using HitAnalyzer2.Enums;
using System.Collections.Generic;
using System.Linq;

namespace HitAnalyzer2.Models
{
    internal record TargetModel
    {
        internal string? TargetName { get; init; }
        internal TargetTypeEnum TargetType { get; init; }
        internal IReadOnlyList<TargetRingModel> Rings { get; init; } = [];
        internal double? MarkedRingPoints { get; init; }
        internal double? MaxDiameter => Rings.Count == 0 ? null : Rings.Max(ring => ring.Diameter);
        internal double? MaxDiameterMm => MaxDiameter * (TargetType == TargetTypeEnum.CircleImperial ? 25.4 : 1);
    }
}
