using HitAnalyzer2.Enums;
using HitAnalyzer2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitAnalyzer2.Repository
{
    internal sealed class TargetFileRepository(string directory)
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter<TargetTypeEnum>() }
        };

        internal IReadOnlyList<(string FileName, TargetModel Target)> LoadTargets(out string? loadError)
        {
            loadError = null;
            string[] paths;
            try
            {
                paths = Directory.GetFiles(directory, "*.json");
            }
            catch (DirectoryNotFoundException)
            {
                return [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                loadError = ex.Message;
                return [];
            }

            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            var targets = new List<(string, TargetModel)>(paths.Length);
            foreach (var path in paths)
            {
                var fileName = Path.GetFileName(path);
                try
                {
                    using var stream = File.OpenRead(path);
                    var data = JsonSerializer.Deserialize<TargetFile>(stream, JsonOptions);
                    if (data is null || string.IsNullOrWhiteSpace(data.TargetName) ||
                        data.TargetType is not (TargetTypeEnum.CircleMetric or TargetTypeEnum.CircleImperial) ||
                        data.Rings is not { Length: > 0 } ||
                        data.Rings.Any(ring => ring is null || !double.IsFinite(ring.Diameter) ||
                            ring.Diameter <= 0 || !double.IsFinite(ring.PointValue)))
                        throw new JsonException("Invalid circular target.");

                    targets.Add((fileName, new TargetModel
                    {
                        TargetName = data.TargetName,
                        TargetType = data.TargetType,
                        Rings = Array.AsReadOnly(data.Rings),
                        MarkedRingPoints = data.MarkedRingPoints
                    }));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    loadError ??= $"Could not load {fileName}. {ex.Message}";
                }
            }

            return targets.AsReadOnly();
        }

        private sealed record TargetFile
        {
            public required string TargetName { get; init; }
            public required TargetTypeEnum TargetType { get; init; }
            public required TargetRingModel[] Rings { get; init; }
            public double? MarkedRingPoints { get; init; }
        }
    }
}
