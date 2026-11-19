using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Models;
using HitAnalyzer2.Models.Shooting;
using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Services.Recording;
using HitAnalyzer2.Services.Rendering;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Target;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HitAnalyzer2.ViewModels
{
    public sealed record ScoreHistogramBin(double Score, int Count, double BarHeight);

    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly SettingsService _settings;
        private readonly ShootingSessionService _shootingSession;
        private readonly RecordingService _recording;
        private readonly DetectionSettings _detectionSettings;
        private SessionResult? _sessionResult;
        private HitGroupCircle? _smallestHitGroup;
        private CancellationTokenSource? _hitGroupCalculation;

        private sealed record SessionResult(TargetModel Target, double CaliberMm, int PreviewSide,
            IReadOnlyList<HitRecord> Hits, Position[] Points, HitGroupCircle? Cep50, HitGroupCircle? Cep80);
        internal volatile bool IsTargetPreviewActive = true;
        internal volatile bool IsWeaponPreviewActive;
        internal volatile bool IsDarkTheme;

        public CameraSettingsViewModel CameraSettings { get; }
        public CameraPreviewViewModel CameraPreview { get; } = new();
        public DetectionSettingsViewModel DetectionSettings { get; }
        public ShootingSettingsViewModel ShootingSettings { get; }
        public WeaponSettingsViewModel WeaponSettings { get; }
        public TargetSettingsViewModel TargetSettings { get; }

        [ObservableProperty]
        private string? configurationMessage;
        [ObservableProperty]
        private Bitmap? sessionResultsImage;
        [ObservableProperty]
        private string? meanHitGroupText;
        [ObservableProperty]
        private string? cep50Text;
        [ObservableProperty]
        private string? smallestHitGroupText;
        [ObservableProperty]
        private string? meanPointOfImpactText;
        [ObservableProperty]
        private IReadOnlyList<ScoreHistogramBin> scoreHistogram = [];

        internal MainWindowViewModel(SettingsService settings, DetectionSettingsViewModel detectionSettings,
            WeaponSettingsViewModel weaponSettings, TargetSettingsViewModel targetSettings, ShootingSessionService shootingSession,
            TargetCalibrationService cameraCalibration, RecordingService recording)
        {
            _settings = settings;
            configurationMessage = settings.LoadError;
            DetectionSettings = detectionSettings;
            _detectionSettings = settings.Current.Detection;
            WeaponSettings = weaponSettings;
            TargetSettings = targetSettings;
            _shootingSession = shootingSession;
            _recording = recording;
            ShootingSettings = new ShootingSettingsViewModel(settings, shootingSession, recording, SaveConfiguration);
            TargetSettings.SetShootingSettings(ShootingSettings);
            var captureSession = new CameraCaptureSession(
                ProcessCameraFrameAsync,
                () =>
                {
                    shootingSession.ClearAimPaths();
                    cameraCalibration.UpdatePreviewGeometry(0, 0);
                    CameraPreview.ClearPreview();
                },
                message => CameraPreview.StatusMessage = message);

            CameraSettings = new CameraSettingsViewModel(settings, captureSession, cameraCalibration);
            ShootingSettings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ShootingSettingsViewModel.IsSessionComplete) or
                    nameof(ShootingSettingsViewModel.ShowSmallestHitGroup))
                    RefreshSessionResults();
            };
        }

        internal void RefreshSessionResults()
        {
            var status = _shootingSession.GetSessionStatus();
            if (status.EndReason is null)
            {
                StopHitGroupCalculation();
                _sessionResult = null;
                _smallestHitGroup = null;
                SetSessionResultsImage(null);
                MeanHitGroupText = null;
                Cep50Text = null;
                SmallestHitGroupText = null;
                MeanPointOfImpactText = null;
                ScoreHistogram = [];
                return;
            }

            if (_sessionResult is null)
            {
                var previewSize = CameraPreview.WebcamImage?.PixelSize;
                int side = previewSize is { } size ? Math.Min(size.Width, size.Height) : 480;
                var hits = _recording.GetNormalizedHitRecordList();
                var points = hits.Select(hit => (Position)hit.HitPosition).ToArray();
                (HitGroupCircle? Cep50, HitGroupCircle? Cep80) circles =
                    points.Length > 0 ? HitGroup.AroundMean(points) : (null, null);
                var target = CameraSettings.Calibration.Settings;
                _sessionResult = new(target, status.LastHitCaliberMm, side,
                    hits, points, circles.Cep50, circles.Cep80);
                ScoreHistogram = BuildScoreHistogram(hits, target);
            }

            if (!ShowEnhancedR80)
                StopHitGroupCalculation();
            else if (_smallestHitGroup is null && _hitGroupCalculation is null && _sessionResult.Cep80 is { } mean)
            {
                _hitGroupCalculation = new CancellationTokenSource();
                _ = CalculateSmallestHitGroupAsync(_sessionResult, mean, _hitGroupCalculation);
            }
            RenderSessionResults(_sessionResult);
        }

        private static IReadOnlyList<ScoreHistogramBin> BuildScoreHistogram(IReadOnlyList<HitRecord> hits, TargetModel target)
        {
            var counts = hits.GroupBy(hit => hit.HitPosition.Score)
                .ToDictionary(group => group.Key, group => group.Count());
            counts.TryAdd(0, 0);
            foreach (var ring in target.Rings)
                counts.TryAdd(ring.PointValue, 0);

            int maxCount = counts.Values.Max();
            const double maxBarHeight = 44;
            return counts.OrderBy(bin => bin.Key)
                .Select(bin => new ScoreHistogramBin(bin.Key, bin.Value,
                    bin.Value == 0 ? 0 : Math.Max(1, maxBarHeight * bin.Value / maxCount)))
                .ToArray();
        }

        private bool ShowEnhancedR80 => ShootingSettings.IsEnhancedR80Available && ShootingSettings.ShowSmallestHitGroup;

        private async Task CalculateSmallestHitGroupAsync(SessionResult result, HitGroupCircle mean,
            CancellationTokenSource calculation)
        {
            try
            {
                var circle = await Task.Run(() => HitGroup.SmallestCircle(result.Points, mean, calculation.Token),
                    calculation.Token);
                if (ReferenceEquals(_hitGroupCalculation, calculation))
                {
                    _hitGroupCalculation = null;
                    _smallestHitGroup = circle;
                    RenderSessionResults(result);
                }
            }
            catch (OperationCanceledException) when (calculation.IsCancellationRequested)
            {
            }
            finally
            {
                calculation.Dispose();
            }
        }

        private void StopHitGroupCalculation()
        {
            _hitGroupCalculation?.Cancel();
            _hitGroupCalculation = null;
        }

        private void RenderSessionResults(SessionResult result)
        {
            var smallest = ShowEnhancedR80 ? _smallestHitGroup : null;
            using var image = new Mat(result.PreviewSide, result.PreviewSide, MatType.CV_8UC4);
            TargetRenderer.DrawTargetPreview(image, null, result.Target, result.CaliberMm, result.Hits, IsDarkTheme,
                cep50: result.Cep50, cep80: result.Cep80, smallestCircle: smallest);
            SetSessionResultsImage(CameraPreviewViewModel.CreateBitmap(image));
            Cep50Text = FormatGroup("CEP50 (R50)", result.Cep50);
            MeanHitGroupText = FormatGroup("CEP80 (R80)", result.Cep80);
            SmallestHitGroupText = _hitGroupCalculation is not null
                ? "Enhanced R80: calculating…"
                : FormatGroup("Enhanced R80", smallest);
            MeanPointOfImpactText = null;
            if (result.Cep80 is { } group &&
                result.Target.MaxDiameterMm is > 0 and < double.PositiveInfinity and var targetDiameterMm &&
                result.Target.GetDistanceFromCenterMm(group.Center) is { } deviationMm && double.IsFinite(deviationMm))
            {
                // Match the offset controls: positive X is right and positive Y is up.
                double xMm = Math.Round((group.Center.X - TargetModel.Center.X) * targetDiameterMm, 1);
                double yMm = Math.Round((TargetModel.Center.Y - group.Center.Y) * targetDiameterMm, 1);
                MeanPointOfImpactText =
                    $"MPI: {deviationMm:0.0} mm (X {xMm:+0.0;-0.0;0.0} mm, Y {yMm:+0.0;-0.0;0.0} mm)";
            }

            string? FormatGroup(string label, HitGroupCircle? circle)
            {
                if (circle is null)
                    return null;

                string coverage = $"{circle.CoveredHits}/{result.Points.Length} hits";
                return result.Target.MaxDiameterMm is > 0 and < double.PositiveInfinity and var diameterMm
                    ? $"{label} · Ø {2 * circle.Radius * diameterMm:0.0} mm · {coverage}"
                    : $"{label} · {coverage}";
            }
        }

        private void SetSessionResultsImage(Bitmap? image)
        {
            var previous = SessionResultsImage;
            SessionResultsImage = image;
            previous?.Dispose();
        }

        internal Task RefreshCamerasAsync() => CameraSettings.RefreshCamerasAsync();

        internal Task StopCameraAsync() => CameraSettings.StopCameraAsync();

        [RelayCommand]
        private void SaveConfiguration()
        {
            var current = _settings.Current;
            var camera = CameraSettings.GetConfiguration(current.Camera);
            var detection = DetectionSettings.GetConfiguration(current.Detection);
            var weapon = WeaponSettings.GetSelectedProfile();
            var configuration = current with
            {
                Theme = ShootingSettings.SelectedTheme,
                Shooting = ShootingSettings.GetConfiguration(current.Shooting),
                Camera = camera,
                Target = TargetSettings.GetConfiguration(),
                Detection = detection,
                SelectedWeapon = weapon ?? current.SelectedWeapon,
                LegacyWeapon = weapon is null && current.SelectedWeapon is null
                    ? WeaponSettings.GetConfiguration(current.LegacyWeapon ?? new WeaponSettings())
                    : null
            };

            try
            {
                _settings.SaveConfiguration(configuration, weapon);
                if (weapon is not null && WeaponSettings.SelectedWeapon is { } selected)
                    selected.Profile = weapon.Profile;
                ConfigurationMessage = "Settings saved.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                ConfigurationMessage = $"Could not save settings. {ex.Message}";
            }
        }

        private async Task ProcessCameraFrameAsync(Mat frame, long capturedAt, double detectedFps, string measuredFps,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CameraSettings.Calibration.UpdatePreviewGeometry(frame.Width, frame.Height);
            using var output = new Mat();
            var pixels = FramePreprocessor.ApplyImageAdjustments(frame, output, DetectionSettings.BrightnessPercent,
                DetectionSettings.ContrastPercent, DetectionSettings.SaturationPercent);
            bool showTargetPreview = IsTargetPreviewActive;
            bool showWeaponOffsets = IsWeaponPreviewActive;
            var (lastHit, aimPath, followPath, lastTargetHit, caliberDiameterMm) =
                _shootingSession.ProcessFrame(pixels, capturedAt, detectedFps, cancellationToken);
            if (showTargetPreview)
            {
                int size = Math.Min(pixels.Rows, pixels.Cols);
                using var target = new Mat(size, size, pixels.Type());
                Position? originalTargetHit = null;
                double? previewScore = null;
                double previewCaliberMm = showWeaponOffsets ? WeaponSettings.CaliberDiameterMm : caliberDiameterMm;
                if (showWeaponOffsets && lastHit is { } weaponHit)
                {
                    var laser = weaponHit.LaserPosition;
                    var calibration = CameraSettings.Calibration;
                    originalTargetHit = calibration.MapHit(laser.X / pixels.Width, laser.Y / pixels.Height);
                    lastTargetHit = calibration.MapHit(weaponHit.X / pixels.Width, weaponHit.Y / pixels.Height,
                        out var targetAtHit);
                    if (lastTargetHit is { } correctedHit)
                        previewScore = targetAtHit.CalculateScore(correctedHit, previewCaliberMm);
                }

                var recordedHits = !showWeaponOffsets && ShootingSettings.ShowAllHits
                    ? _recording.GetNormalizedHitRecordList()
                    : null;
                TargetRenderer.DrawTargetPreview(target, lastTargetHit, CameraSettings.Calibration.Settings,
                    previewCaliberMm, recordedHits, IsDarkTheme, originalTargetHit);
                await CameraPreview.ShowFrameAsync(target, null, cancellationToken, previewScore,
                    showWeaponOffsets ? lastTargetHit : null);
                return;
            }

            int? flareSensitivity = DetectionSettings.ShowFlares ? DetectionSettings.DetectionSensitivity : null;
            bool showAllowedArea = DetectionSettings.ShowAllowedArea;

            if (ReferenceEquals(pixels, frame) &&
                (lastHit.HasValue || flareSensitivity.HasValue || showAllowedArea))
            {
                frame.CopyTo(output);
                pixels = output;
            }

            if (flareSensitivity is { } sensitivity)
                FlareDetectionRenderer.Draw(pixels, sensitivity);

            if (showAllowedArea)
                AllowedAreaRenderer.Draw(pixels, _detectionSettings);

            if (lastHit is { } hit)
                AimTraceRenderer.DrawHitAndTraceIntoFrame(pixels, hit, aimPath, followPath,
                    caliberDiameterMm / CameraSettings.Calibration.Settings.MmToPixel);

            await CameraPreview.ShowFrameAsync(pixels, measuredFps, cancellationToken);
        }
    }
}
