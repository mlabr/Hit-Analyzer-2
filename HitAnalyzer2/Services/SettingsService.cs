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

        public SettingsService(string path, string weaponsDirectory)
        {
            _path = path;
            WeaponsDirectory = weaponsDirectory;
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
                    ValidateWeaponProfile(savedWeapon.Profile);
                    InitialWeapon = savedWeapon.Profile;
                    RestoreWeaponProfile(savedWeapon);
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
        internal string WeaponsDirectory { get; }

        internal static WeaponProfile LoadWeaponProfile(string path)
        {
            using var stream = File.OpenRead(path);
            var profile = JsonSerializer.Deserialize<WeaponProfile>(stream, JsonOptions);
            ValidateWeaponProfile(profile);
            return profile!;
        }

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

        internal void SaveWeapon(string fileName, WeaponProfile profile)
        {
            lock (_saveGate)
            {
                var profilePath = GetWeaponProfilePath(fileName);
                ValidateWeaponProfile(profile);
                var settings = _current with
                {
                    LegacyWeapon = null,
                    SelectedWeapon = new SavedWeaponProfile(fileName, profile)
                };

                Validate(settings);
                var temporarySettingsPath = WriteTemporaryFile(_path, settings);
                try
                {
                    var temporaryProfilePath = WriteTemporaryFile(profilePath, profile);
                    var previousProfilePath = $"{temporaryProfilePath}.bak";
                    try
                    {
                        bool hadProfile = File.Exists(profilePath);
                        if (hadProfile)
                            File.Replace(temporaryProfilePath, profilePath, previousProfilePath);
                        else
                            File.Move(temporaryProfilePath, profilePath);

                        try
                        {
                            File.Move(temporarySettingsPath, _path, overwrite: true);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            RollBackWeaponProfile(profilePath, previousProfilePath, hadProfile);
                            throw;
                        }

                        _current = settings;
                        File.Delete(previousProfilePath);
                    }
                    finally
                    {
                        File.Delete(temporaryProfilePath);
                    }
                }
                finally
                {
                    File.Delete(temporarySettingsPath);
                }
            }
        }

        private static void RollBackWeaponProfile(string profilePath, string previousProfilePath, bool hadProfile)
        {
            if (hadProfile)
                File.Move(previousProfilePath, profilePath, overwrite: true);
            else
                File.Delete(profilePath);
        }

        private void RestoreWeaponProfile(SavedWeaponProfile savedWeapon)
        {
            var path = GetWeaponProfilePath(savedWeapon.FileName);
            if (File.Exists(path))
                return;

            try
            {
                WriteFile(path, savedWeapon.Profile, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another instance restored the profile first.
            }
        }

        private string GetWeaponProfilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.Contains('/') || fileName.Contains('\\')
                || !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
                throw new JsonException("Neplatný název souboru profilu zbraně.");

            return Path.Combine(WeaponsDirectory, fileName);
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
            WriteFile(_path, settings, overwrite);
        }

        private static void WriteFile<T>(string path, T data, bool overwrite)
        {
            var temporaryPath = WriteTemporaryFile(path, data);
            try
            {
                File.Move(temporaryPath, path, overwrite);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        private static string WriteTemporaryFile<T>(string path, T data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(stream, data, JsonOptions);
                return temporaryPath;
            }
            catch
            {
                File.Delete(temporaryPath);
                throw;
            }
        }

        private static void Validate(AppSettings? settings)
        {
            if (settings is not { Camera: { } camera, Detection: { } detection })
                throw new JsonException("Chybí nastavení kamery nebo detekce.");

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

            if (settings.SelectedWeapon is null && settings.LegacyWeapon is { } legacyWeapon)
                ValidateWeapon(legacyWeapon);
        }

        private static void ValidateWeapon(WeaponSettings weapon)
        {
            if (!Enum.IsDefined(weapon.Trigger)
                || weapon.NexDetectionInterval is < ShootingSessionService.MinimumDetectionInterval
                    or > ShootingSessionService.MaximumDetectionInterval
                || weapon.OffsetX is < WeaponHandler.MinimumOffset or > WeaponHandler.MaximumOffset
                || weapon.OffsetY is < WeaponHandler.MinimumOffset or > WeaponHandler.MaximumOffset)
                throw new JsonException("Neplatné nastavení zbraně.");
        }

        private static void ValidateWeaponProfile(WeaponProfile? profile)
        {
            if (profile is not { Type: "ha_weapon" } || string.IsNullOrWhiteSpace(profile.Name))
                throw new JsonException("Neplatný typ nebo název profilu zbraně.");

            ValidateWeapon(profile);
        }
    }
}
