using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitAnalyzer2
{
    internal sealed class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter<FlipMode>(), new JsonStringEnumConverter<DetectionMethod>() }
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
                var settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions);
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

        public void SaveDetection(DetectionMethod method, int sensitivity)
        {
            lock (_saveGate)
            {
                var settings = _current with
                {
                    Evaluation = _current.Evaluation with { Method = method, DetectionSensitivity = sensitivity }
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
            if (settings is not { Camera: { } camera, Evaluation: { } evaluation })
                throw new JsonException("Chybí nastavení kamery nebo vyhodnocování.");

            if (camera.DeviceIndex is < 0 || camera.Flip is FlipMode flip && !Enum.IsDefined(flip)
                || camera.BrightnessPercent is < 0 or > 100
                || camera.ContrastPercent is < 0 or > 100
                || camera.SaturationPercent is < 0 or > 100
                || camera.Mode is { } mode && (mode.Width <= 0 || mode.Height <= 0
                    || mode.FpsNumerator == 0 || mode.FpsDenominator == 0))
                throw new JsonException("Neplatné nastavení kamery.");

            if (!Enum.IsDefined(evaluation.Method)
                || evaluation.DetectionSensitivity is < 1 or > 99
                || evaluation.MinimumHitAreaPixels is <= 0)
                throw new JsonException("Neplatné parametry vyhodnocování.");
        }
    }
}
