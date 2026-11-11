using HitAnalyzer2.Services.Shooting;
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
        public DetectionSettings Detection { get; init; } = new();
        public WeaponSettings Weapon { get; init; } = new();
    }

    internal sealed record WeaponSettings
    {
        public HitTrigger Trigger { get; init; } = HitTrigger.FirstSpot;
        public int NexDetectionInterval { get; init; } = ShootingSessionService.MinimumDetectionInterval;
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

    internal sealed record DetectionSettings
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
        public bool ShowFlares { get; init; }
    }
}
