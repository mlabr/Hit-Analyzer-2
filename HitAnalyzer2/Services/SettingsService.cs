using HitAnalyzer2.Models;
using HitAnalyzer2.Repository;
using OpenCvSharp;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HitAnalyzer2
{
    internal sealed class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { ConfigureCalibrationCoordinates }
            },
            Converters =
            {
                new JsonStringEnumConverter<FlipMode>(),
                new JsonStringEnumConverter<DetectionMethod>(),
                new JsonStringEnumConverter<TriggerTypeEnum>()
            }
        };

        private static void ConfigureCalibrationCoordinates(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Type != typeof(Position))
                return;

            var converter = new CalibrationCoordinateJsonConverter();
            foreach (var property in typeInfo.Properties)
            {
                if (property.Name is nameof(Position.X) or nameof(Position.Y))
                    property.CustomConverter = converter;
            }
        }

        private readonly string _path;
        private readonly WeaponFileRepository _weaponProfiles;
        private readonly object _saveGate = new();
        private volatile AppSettings _current = new();

        public SettingsService(string path, WeaponFileRepository weaponProfiles)
        {
            _path = path;
            _weaponProfiles = weaponProfiles;
            try
            {
                if (!File.Exists(_path))
                    CreateDefaults();

                using var stream = File.OpenRead(_path);
                var json = JsonNode.Parse(stream);
                if (json is JsonObject config && !config.ContainsKey(nameof(AppSettings.Detection))
                    && config.TryGetPropertyValue("Evaluation", out var legacyDetection))
                {
                    config.Remove("Evaluation");
                    config[nameof(AppSettings.Detection)] = legacyDetection;
                }

                MigrateImageAdjustments(json as JsonObject);
                if (json is JsonObject legacyRoot &&
                    legacyRoot.TryGetPropertyValue(nameof(TargetSettings.SelectedTargetFileName), out var legacyTargetFile))
                {
                    legacyRoot.Remove(nameof(TargetSettings.SelectedTargetFileName));
                    if (!legacyRoot.ContainsKey(nameof(AppSettings.Target)))
                        legacyRoot[nameof(AppSettings.Target)] = new JsonObject();
                    if (legacyRoot[nameof(AppSettings.Target)] is JsonObject targetSection &&
                        !targetSection.ContainsKey(nameof(TargetSettings.SelectedTargetFileName)))
                        targetSection[nameof(TargetSettings.SelectedTargetFileName)] = legacyTargetFile;
                }
                if (json is JsonObject root &&
                    root[nameof(AppSettings.Camera)] is JsonObject cameraSettings &&
                    cameraSettings.TryGetPropertyValue(nameof(TargetSettings.Calibration), out var legacyCalibration))
                {
                    cameraSettings.Remove(nameof(TargetSettings.Calibration));
                    if (!root.ContainsKey(nameof(AppSettings.Target)))
                        root[nameof(AppSettings.Target)] = new JsonObject();
                    if (root[nameof(AppSettings.Target)] is JsonObject targetSettings &&
                        !targetSettings.ContainsKey(nameof(TargetSettings.Calibration)))
                        targetSettings[nameof(TargetSettings.Calibration)] = legacyCalibration;
                }
                if (json?[nameof(AppSettings.Detection)] is JsonObject detection &&
                    !detection.ContainsKey(nameof(DetectionSettings.ShowTargetPreview)) &&
                    detection["ShowAimPointPreview"] is { } legacyPreview)
                {
                    detection[nameof(DetectionSettings.ShowTargetPreview)] = legacyPreview.DeepClone();
                }

                var settings = json.Deserialize<AppSettings>(JsonOptions);
                Validate(settings);
                _current = settings!.SelectedWeapon is null ? settings : settings with { LegacyWeapon = null };
                InitialWeapon = _current.LegacyWeapon ?? new WeaponSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                LoadError = $"Could not load or create settings. Using defaults. {ex.Message}";
            }

            try
            {
                if (_current.SelectedWeapon is { } savedWeapon)
                {
                    WeaponFileRepository.ValidateProfile(savedWeapon.Profile);
                    InitialWeapon = savedWeapon.Profile;
                    _weaponProfiles.RestoreMissingProfile(savedWeapon);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                LoadError = $"Could not restore the saved weapon profile. {ex.Message}";
            }
        }

        public AppSettings Current => _current;
        public string? LoadError { get; }
        internal WeaponSettings InitialWeapon { get; } = new();

        internal void SaveConfiguration(AppSettings settings, SavedWeaponProfile? weapon)
        {
            lock (_saveGate)
            {
                if (weapon is null)
                {
                    Write(settings, overwrite: true);
                    _current = settings;
                    return;
                }

                Validate(settings);
                var temporarySettingsPath = JsonFile.WriteTemporaryFile(_path, settings, JsonOptions);
                try
                {
                    _weaponProfiles.SaveProfile(weapon, () =>
                    {
                        File.Move(temporarySettingsPath, _path, overwrite: true);
                        _current = settings;
                    });
                }
                finally
                {
                    File.Delete(temporarySettingsPath);
                }
            }
        }

        private void CreateDefaults()
        {
            try
            {
                Write(_current, overwrite: false);
            }
            catch (IOException) when (File.Exists(_path))
            {
                // Another instance created the file first. Read its settings.
            }
        }

        private void Write(AppSettings settings, bool overwrite)
        {
            Validate(settings);
            JsonFile.Write(_path, settings, overwrite, JsonOptions);
        }

        private static void MigrateImageAdjustments(JsonObject? settings)
        {
            if (settings?[nameof(AppSettings.Camera)] is not JsonObject camera)
                return;

            if (!settings.ContainsKey(nameof(AppSettings.Detection)))
                settings[nameof(AppSettings.Detection)] = new JsonObject();

            if (settings[nameof(AppSettings.Detection)] is not JsonObject detection)
                return;

            foreach (string name in new[]
            {
                nameof(DetectionSettings.BrightnessPercent),
                nameof(DetectionSettings.ContrastPercent),
                nameof(DetectionSettings.SaturationPercent)
            })
            {
                if (!detection.ContainsKey(name) && camera[name] is { } value)
                    detection[name] = value.DeepClone();
            }
        }

        private static void Validate(AppSettings? settings)
        {
            if (settings is not { Camera: { } camera, Detection: { } detection })
                throw new JsonException("Camera or detection settings are missing.");

            if (settings.Target is not { CalibrationZoom: >= 2 and <= 4, Calibration: not null })
                throw new JsonException("Invalid target calibration zoom.");

            if (camera.DeviceIndex is < 0 || camera.Flip is FlipMode flip && !Enum.IsDefined(flip)
                || camera.Mode is { } mode && (mode.Width <= 0 || mode.Height <= 0
                    || mode.FpsNumerator == 0 || mode.FpsDenominator == 0))
                throw new JsonException("Invalid camera settings.");

            if (!Enum.IsDefined(detection.Method)
                || detection.BrightnessPercent is < 0 or > 100
                || detection.ContrastPercent is < 0 or > 100
                || detection.SaturationPercent is < 0 or > 100
                || detection.DetectionSensitivity is < 1 or > 99
                || detection.MinimumHitAreaPixels is <= 0)
                throw new JsonException("Invalid detection parameters.");

            if (settings.SelectedWeapon is null && settings.LegacyWeapon is { } legacyWeapon)
                WeaponFileRepository.ValidateSettings(legacyWeapon);
        }
    }
}
