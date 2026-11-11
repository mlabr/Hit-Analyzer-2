using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitAnalyzer2
{
    internal sealed record AppSettings
    {
        public CameraSettings Camera { get; init; } = new();
        public EvaluationSettings Evaluation { get; init; } = new();
    }

    internal sealed record CameraSettings
    {
        public string? DeviceId { get; init; }
        public int? DeviceIndex { get; init; }
        public CameraMode? Mode { get; init; }
        public FlipMode? Flip { get; init; } = FlipMode.XY;
        public int BrightnessPercent { get; init; } = 50;
        public int ContrastPercent { get; init; } = 50;
        public int SaturationPercent { get; init; } = 50;
    }

    public enum DetectionMethod
    {
        Simple,
        Blob
    }

    internal sealed record EvaluationSettings
    {
        private const int DefaultDetectionSensitivity = 12;
        private int? _detectionSensitivity;
        private int? _legacyDetectionSensitivity;

        public DetectionMethod Method { get; init; } = DetectionMethod.Simple;
        public int DetectionSensitivity
        {
            get => _detectionSensitivity ?? _legacyDetectionSensitivity ?? DefaultDetectionSensitivity;
            init => _detectionSensitivity = value;
        }

        // Read legacy settings and write the sensitivity on the next save.
        [JsonInclude]
        [JsonPropertyName("NormalizedDetectionThreshold")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        private double? LegacyDetectionThreshold
        {
            get => null;
            init
            {
                if (value is not double threshold)
                    return;

                if (!double.IsFinite(threshold) || threshold is < 0 or > 1)
                    throw new JsonException("Neplatný práh detekce.");

                _legacyDetectionSensitivity = Math.Clamp(byte.MaxValue - (int)Math.Ceiling(threshold * byte.MaxValue), 1, 99);
            }
        }

        public int? MinimumHitAreaPixels { get; init; }
    }
}
