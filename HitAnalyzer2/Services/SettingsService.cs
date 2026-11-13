using HitAnalyzer2.Repository;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Target;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HitAnalyzer2
{
    internal sealed class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter<FlipMode>(),
                new JsonStringEnumConverter<DetectionMethod>(),
                new JsonStringEnumConverter<TriggerTypeEnum>()
            }
        };

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
                LoadError = $"Nastavení se nepodařilo načíst nebo vytvořit. Používám výchozí hodnoty. {ex.Message}";
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
                LoadError = $"Uložený profil zbraně se nepodařilo obnovit. {ex.Message}";
            }
        }

        public AppSettings Current => _current;
        public string? LoadError { get; }
        internal WeaponSettings InitialWeapon { get; } = new();

        public void SaveCamera(CameraDevice device, CameraMode? mode, FlipMode? flip,
            CameraCalibrationSettings? calibration = null)
        {
            lock (_saveGate)
            {
                var settings = _current with
                {
                    Camera = _current.Camera with
                    {
                        DeviceId = device.DeviceId,
                        DeviceIndex = device.DeviceId is null ? device.Index : null,
                        Mode = mode,
                        Flip = flip,
                        Calibration = calibration ?? (MatchesCamera(_current.Camera, device, mode, flip)
                            ? _current.Camera.Calibration : new())
                    }
                };
                Write(settings, overwrite: true);
                _current = settings;
            }
        }

        private static bool MatchesCamera(CameraSettings camera, CameraDevice device, CameraMode? mode, FlipMode? flip) =>
            camera.DeviceId == device.DeviceId && (device.DeviceId is not null || camera.DeviceIndex == device.Index) &&
            camera.Mode == mode && camera.Flip == flip;

        public void SaveTargetPreview(bool show)
        {
            lock (_saveGate)
            {
                var settings = _current with { Detection = _current.Detection with { ShowTargetPreview = show } };
                Write(settings, overwrite: true);
                _current = settings;
            }
        }

        public void SaveDetection(DetectionMethod method, int sensitivity, bool showFlares, bool showAllowedArea,
            int brightnessPercent, int contrastPercent, int saturationPercent)
        {
            lock (_saveGate)
            {
                var settings = _current with
                {
                    Detection = _current.Detection with
                    {
                        Method = method,
                        DetectionSensitivity = sensitivity,
                        ShowFlares = showFlares,
                        ShowAllowedArea = showAllowedArea,
                        BrightnessPercent = brightnessPercent,
                        ContrastPercent = contrastPercent,
                        SaturationPercent = saturationPercent
                    }
                };
                Write(settings, overwrite: true);
                _current = settings;
            }
        }

        internal void SaveWeapon(string fileName, WeaponProfile profile)
        {
            lock (_saveGate)
            {
                var selectedWeapon = new SavedWeaponProfile(fileName, profile);
                var settings = _current with
                {
                    LegacyWeapon = null,
                    SelectedWeapon = selectedWeapon
                };

                Validate(settings);
                var temporarySettingsPath = JsonFile.WriteTemporaryFile(_path, settings, JsonOptions);
                try
                {
                    _weaponProfiles.SaveProfile(selectedWeapon, () =>
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
                throw new JsonException("Chybí nastavení kamery nebo detekce.");

            if (camera.DeviceIndex is < 0 || camera.Flip is FlipMode flip && !Enum.IsDefined(flip)
                || camera.Calibration is null
                || camera.Mode is { } mode && (mode.Width <= 0 || mode.Height <= 0
                    || mode.FpsNumerator == 0 || mode.FpsDenominator == 0))
                throw new JsonException("Neplatné nastavení kamery.");

            if (!Enum.IsDefined(detection.Method)
                || detection.BrightnessPercent is < 0 or > 100
                || detection.ContrastPercent is < 0 or > 100
                || detection.SaturationPercent is < 0 or > 100
                || detection.DetectionSensitivity is < 1 or > 99
                || detection.MinimumHitAreaPixels is <= 0)
                throw new JsonException("Neplatné parametry detekce.");

            if (settings.SelectedWeapon is null && settings.LegacyWeapon is { } legacyWeapon)
                WeaponFileRepository.ValidateSettings(legacyWeapon);
        }
    }
}
