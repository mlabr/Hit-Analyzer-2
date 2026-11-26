using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitAnalyzer2.Models
{
    internal sealed record WeaponProfileModel : WeaponSettings
    {
        public required string Type { get; init; }
        public required string Name { get; init; }
        public decimal Caliber { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
    }
}
