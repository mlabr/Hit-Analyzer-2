using HitAnalyzer2.Services.Shooting;
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
                new JsonStringEnumConverter<HitTrigger>()
            }
        };

        private readonly string _path;
        private readonly object _saveGate = new();
        private volatile AppSettings _current = new();

        public SettingsService(string path)
        {
            _path = path;
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

                var settings = json.Deserialize<AppSettings>(JsonOptions);
                Validate(settings);
                _current = settings!;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                LoadError = $"Nastavení se nepodařilo načíst nebo vytvořit. Používám výchozí hodnoty. {ex.Message}";
            }
        }

        public AppSettings Current => _current;
        public string? LoadError { get; }

        public void SaveCamera(CameraDevice device, CameraMode? mode, FlipMode? flip,
            int brightnessPercent, int contrastPercent, int saturationPercent)
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
                        BrightnessPercent = brightnessPercent,
                        ContrastPercent = contrastPercent,
                        SaturationPercent = saturationPercent
                    }
                };
                Write(settings, overwrite: true);
                _current = settings;
            }
        }

        public void SaveDetection(DetectionMethod method, int sensitivity, bool showFlares)
        {
            lock (_saveGate)
            {
                var settings = _current with
                {
                    Detection = _current.Detection with
                    {
                        Method = method,
                        DetectionSensitivity = sensitivity,
                        ShowFlares = showFlares
                    }
                };
                Write(settings, overwrite: true);
                _current = settings;
            }
        }

        public void SaveWeapon(HitTrigger trigger, int interval)
        {
            lock (_saveGate)
            {
                var settings = _current with
                {
                    Weapon = _current.Weapon with { Trigger = trigger, NexDetectionInterval = interval }
                };
                Write(settings, overwrite: true);
                _current = settings;
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
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                    JsonSerializer.Serialize(stream, settings, JsonOptions);

                File.Move(temporaryPath, _path, overwrite);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        private static void Validate(AppSettings? settings)
        {
            if (settings is not { Camera: { } camera, Detection: { } detection, Weapon: { } weapon })
                throw new JsonException("Chybí nastavení kamery, detekce nebo zbraně.");

            if (camera.DeviceIndex is < 0 || camera.Flip is FlipMode flip && !Enum.IsDefined(flip)
                || camera.BrightnessPercent is < 0 or > 100
                || camera.ContrastPercent is < 0 or > 100
                || camera.SaturationPercent is < 0 or > 100
                || camera.Mode is { } mode && (mode.Width <= 0 || mode.Height <= 0
                    || mode.FpsNumerator == 0 || mode.FpsDenominator == 0))
                throw new JsonException("Neplatné nastavení kamery.");

            if (!Enum.IsDefined(detection.Method)
                || detection.DetectionSensitivity is < 1 or > 99
                || detection.MinimumHitAreaPixels is <= 0)
                throw new JsonException("Neplatné parametry detekce.");

            if (!Enum.IsDefined(weapon.Trigger)
                || weapon.NexDetectionInterval is < ShootingSessionService.MinimumDetectionInterval
                    or > ShootingSessionService.MaximumDetectionInterval)
                throw new JsonException("Neplatné nastavení zbraně.");
        }
    }
}
