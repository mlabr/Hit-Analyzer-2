using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitAnalyzer2
{
    internal sealed record AppSettings
    {
        public ThemePreference Theme { get; init; } = ThemePreference.System;
        public ShootingSettings Shooting { get; init; } = new();
        public CameraSettings Camera { get; init; } = new();
        public TargetSettings Target { get; init; } = new();
        public AudioSettings Audio { get; init; } = new();
        public DetectionSettings Detection { get; init; } = new();
        public AppLinkSettings AppLink { get; init; } = new();
        [JsonPropertyName("Weapon")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WeaponSettings? LegacyWeapon { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SavedWeaponProfile? SelectedWeapon { get; init; }
    }

    internal sealed record AudioSettings
    {
        public bool IsEnabled { get; init; }
        public AudioEffectMode Effect { get; init; } = AudioEffectMode.HitOnly;
        public int EffectVolume { get; init; } = 50;
        public SgaSettings SGA { get; init; } = new();
    }

    internal sealed record SgaSettings
    {
        public bool IsEnabled { get; init; }
        public bool IsStereo { get; init; } = true;
        public bool FocusOnTarget { get; init; }
        public int Volume { get; init; } = 50;
        public double OffTargetFrequencyHz { get; init; } = 200;
        public double OnTargetMinimumFrequencyHz { get; init; } = 400;
        public double OnTargetMaximumFrequencyHz { get; init; } = 1200;
        public double SlowPeriodSeconds { get; init; } = 0.40;
        public double FastPeriodSeconds { get; init; } = 0.10;
        public double QuietChannelGain { get; init; } = 0.15;
        public double DutyCycle { get; init; } = 0.4;
    }

    public enum AudioEffectMode
    {
        HitOnly,
        SpotterScore,
        Spotter8Directions,
        Spotter12Directions
    }

    internal sealed record AppLinkSettings
    {
        internal const string DefaultSid = "00000001";

        public bool IsEnabled { get; init; }
        public int ReceivePort { get; init; } = 45100;
        public int DestinationPort { get; init; } = 45101;
        public string LocalAddress { get; init; } = "127.0.0.1";
        public string DestinationAddress { get; init; } = "127.0.0.1";
        public string Sid { get; init; } = DefaultSid;
    }

    public enum ThemePreference
    {
        System,
        Light,
        Dark
    }

    internal sealed record TargetSettings
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SelectedTargetFileName { get; init; }
        public int CalibrationZoom { get; init; } = 2;
        public decimal RealDistanceMeters { get; init; } = 5;
        public decimal SimulatedDistanceMeters { get; init; } = 10;
        public TargetPreviewModel Calibration { get; init; } = new();
    }

    internal sealed record ShootingSettings
    {
        public string Shooter { get; init; } = string.Empty;
        public bool IsHitLimitEnable { get; init; }
        public int HitLimit { get; init; } = 40;
        public bool IsTimeLimitEnable { get; init; }
        public bool Countdown { get; init; }
        public bool ShowSmallestHitGroup { get; init; }
        public int TimeLimitSeconds { get; init; } = 900;
        public int AimPathTimeLimit { get; init; } = 15;
        public int AimPathTimeoutMs { get; init; } = 400;
        public double AimPathTimeoutMinimumDuration { get; init; } = 2;
        public int AimPathAfterTimeLimit { get; init; } = 4;
    }

    internal sealed record SavedWeaponProfile(string FileName, WeaponProfile Profile);

    internal record WeaponSettings
    {
        public int OffsetX { get; init; }
        public int OffsetY { get; init; }
        public TriggerTypeEnum Trigger { get; init; } = TriggerTypeEnum.FirstSpot;
        public int NexDetectionInterval { get; init; } = ShootingSessionService.NextDetectionMinimumInterval;
    }

    internal sealed record WeaponProfile : WeaponSettings
    {
        public required string Type { get; init; }
        public required string Name { get; init; }
        public decimal Caliber { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
    }

    internal sealed record CameraSettings
    {
        public string? DeviceId { get; init; }
        public int? DeviceIndex { get; init; }
        public CameraMode? Mode { get; init; }
        public FlipMode? Flip { get; init; } = FlipMode.XY;
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
                    throw new JsonException("Invalid detection threshold.");

                _legacyDetectionSensitivity = Math.Clamp(byte.MaxValue - (int)Math.Ceiling(threshold * byte.MaxValue), 1, 99);
            }
        }

        public int BrightnessPercent { get; init; } = 50;
        public int ContrastPercent { get; init; } = 50;
        public int SaturationPercent { get; init; } = 50;
        public int? MinimumHitAreaPixels { get; init; }
        public bool ShowFlares { get; init; }
        public bool ShowAllowedArea { get; init; }
        public bool ShowTargetPreview { get; init; }
        public int VerticalForbiddenZoneSize { get; init; } = 50;
        public int HorizontalForbiddenZoneSize { get; init; } = 50;
    }
}
