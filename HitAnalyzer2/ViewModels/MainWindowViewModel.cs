using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Services.Recording;
using HitAnalyzer2.Services.Rendering;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Statistics;
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
        private GroupStatistic? _smallestHitGroup;
        private CancellationTokenSource? _hitGroupCalculation;

        private sealed record SessionResult(TargetModel Target, double CaliberMm, int PreviewSide,
            IReadOnlyList<HitRecord> Hits, SessionStatistics Statistics);
        internal volatile bool IsTargetPreviewActive = true;
        internal volatile bool IsWeaponPreviewActive;
        internal volatile bool IsDarkTheme;

        public CameraSettingsViewModel CameraSettings { get; }
        public CameraPreviewViewModel CameraPreview { get; } = new();
        public DetectionSettingsViewModel DetectionSettings { get; }
        public ShootingSettingsViewModel ShootingSettings { get; }
        public WeaponSettingsViewModel WeaponSettings { get; }
        public TargetSettingsViewModel TargetSettings { get; }
        public AudioSettingsViewModel AudioSettings { get; }
        public AppLinkViewModel AppLink { get; }

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
            AudioSettings = new AudioSettingsViewModel(settings.Current.Audio);
            shootingSession.GuidanceAimEvaluated += AudioSettings.UpdateGuidance;
            AppLink = new AppLinkViewModel(settings.Current.AppLink ?? new AppLinkSettings());
            _shootingSession = shootingSession;
            _recording = recording;
            ShootingSettings = new ShootingSettingsViewModel(settings, shootingSession, recording, SaveConfiguration);
            shootingSession.MonoscopeHitEvaluated += position =>
                Dispatcher.UIThread.Post(async () =>
                {
                    AudioSettings.PlayHit(null);
                    await AppLink.SendMonoscopeHitAsync(position);
                });
            shootingSession.HitRecorded += (hit, target) =>
                Dispatcher.UIThread.Post(() => AudioSettings.PlayHit(hit, target));
            shootingSession.SessionRunningChanged += running =>
                Dispatcher.UIThread.Post(() => AudioSettings.PlaySessionState(running));
            TargetSettings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TargetSettingsViewModel.SelectedTarget))
                    ShootingSettings.RefreshTargetMode();
            };
            TargetSettings.SetShootingSettings(ShootingSettings);
            var captureSession = new CameraCaptureSession(
                ProcessCameraFrameAsync,
                () =>
                {
                    AudioSettings.UpdateGuidance(double.NaN, null, 0);
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
            if (status.EndReason is null || ShootingSettings.IsMonoscope)
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
                var target = CameraSettings.Calibration.Settings;
                var statistics = StatisticService.Calculate(hits, target);
                _sessionResult = new(target, status.LastHitCaliberMm, side, hits, statistics);
                ScoreHistogram = CreateScoreHistogramBars(statistics.ScoreHistogram);
            }

            if (!ShowEnhancedR80)
                StopHitGroupCalculation();
            else if (_smallestHitGroup is null && _hitGroupCalculation is null &&
                _sessionResult.Statistics.Cep80 is { } cep80)
            {
                _hitGroupCalculation = new CancellationTokenSource();
                _ = CalculateSmallestHitGroupAsync(_sessionResult, cep80, _hitGroupCalculation);
            }
            RenderSessionResults(_sessionResult);
        }

        private static IReadOnlyList<ScoreHistogramBin> CreateScoreHistogramBars(IReadOnlyList<ScoreHistogram> histogram)
        {
            int maxCount = histogram.Max(bin => bin.Count);
            const double maxBarHeight = 44;
            return histogram.Select(bin => new ScoreHistogramBin(bin.Score, bin.Count,
                    bin.Count == 0 ? 0 : Math.Max(1, maxBarHeight * bin.Count / maxCount)))
                .ToArray();
        }

        private bool ShowEnhancedR80 => ShootingSettings.IsEnhancedR80Available && ShootingSettings.ShowSmallestHitGroup;

        private async Task CalculateSmallestHitGroupAsync(SessionResult result, GroupStatistic cep80,
            CancellationTokenSource calculation)
        {
            try
            {
                var circle = await StatisticService.CalculateEnhancedR80Async(result.Hits, result.Target,
                    cep80, calculation.Token);
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
            var statistics = result.Statistics;
            using var image = new Mat(result.PreviewSide, result.PreviewSide, MatType.CV_8UC4);
            TargetRenderer.DrawTargetPreview(image, null, result.Target, result.CaliberMm, result.Hits, IsDarkTheme,
                cep50: statistics.Cep50?.Circle, cep80: statistics.Cep80?.Circle, smallestCircle: smallest?.Circle);
            SetSessionResultsImage(CameraPreviewViewModel.CreateBitmap(image));
            Cep50Text = FormatGroup("CEP50 (R50)", statistics.Cep50);
            MeanHitGroupText = FormatGroup("CEP80 (R80)", statistics.Cep80);
            SmallestHitGroupText = _hitGroupCalculation is not null
                ? "Enhanced R80: calculating…"
                : FormatGroup("Enhanced R80", smallest);
            if (statistics.MeanPointOfImpact is { } mpi)
            {
                double xMm = Math.Round(mpi.XMm, 1);
                double yMm = Math.Round(mpi.YMm, 1);
                MeanPointOfImpactText =
                    $"MPI: {mpi.DistanceMm:0.0} mm (X {xMm:+0.0;-0.0;0.0} mm, Y {yMm:+0.0;-0.0;0.0} mm)";
            }
            else
                MeanPointOfImpactText = null;

            string? FormatGroup(string label, GroupStatistic? group)
            {
                if (group is null)
                    return null;

                string coverage = $"{group.Circle.CoveredHits}/{result.Hits.Count} hits";
                return group.DiameterMm is { } diameterMm
                    ? $"{label} · Ø {diameterMm:0.0} mm · {coverage}"
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

        internal event Action? RecordedHitsChanged
        {
            add => _recording.HitCountChanged += value;
            remove => _recording.HitCountChanged -= value;
        }

        internal Bitmap CreateAudioTargetPreview()
        {
            const int size = 768;
            using var image = new Mat(size, size, MatType.CV_8UC4);
            var caliber = _shootingSession.GetSessionStatus().LastHitCaliberMm;
            var hit = _recording.GetLastHitRecord()?.HitPosition;
            TargetRenderer.DrawTargetPreview(image, hit, CameraSettings.Calibration.Settings, caliber,
                isDarkTheme: IsDarkTheme, directionCount: AudioSettings.DirectionCount);
            return CameraPreviewViewModel.CreateBitmap(image);
        }

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
                Audio = AudioSettings.GetConfiguration(),
                Detection = detection,
                SelectedWeapon = weapon ?? current.SelectedWeapon,
                LegacyWeapon = weapon is null && current.SelectedWeapon is null
                    ? WeaponSettings.GetConfiguration(current.LegacyWeapon ?? new WeaponSettings())
                    : null
            };

            try
            {
                _settings.SaveConfiguration(configuration with { AppLink = AppLink.GetConfiguration() }, weapon);
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
            var (lastHit, aimPath, followPath, lastTargetHit, caliberDiameterMm, currentAim) =
                _shootingSession.ProcessFrame(pixels, capturedAt, detectedFps, cancellationToken);
            if (showTargetPreview)
            {
                var calibration = CameraSettings.Calibration;
                int size = Math.Min(pixels.Rows, pixels.Cols);
                using var target = new Mat(size, size, pixels.Type());
                Position? originalTargetHit = null;
                Position? liveAim = null;
                Position? liveOffsetAim = null;
                double? previewScore = null;
                double previewCaliberMm = showWeaponOffsets ? WeaponSettings.CaliberDiameterMm : caliberDiameterMm;
                if (showWeaponOffsets && currentAim is { } weaponAim)
                {
                    var laser = weaponAim.LaserPosition;
                    liveAim = calibration.MapHit(laser.X / pixels.Width, laser.Y / pixels.Height);
                    liveOffsetAim = calibration.MapHit(weaponAim.X / pixels.Width, weaponAim.Y / pixels.Height);
                }

                if (showWeaponOffsets && lastHit is { } weaponHit)
                {
                    var laser = weaponHit.LaserPosition;
                    originalTargetHit = calibration.MapHit(laser.X / pixels.Width, laser.Y / pixels.Height);
                    lastTargetHit = calibration.MapHit(weaponHit.X / pixels.Width, weaponHit.Y / pixels.Height,
                        out var targetAtHit);
                    if (lastTargetHit is { } correctedHit)
                        previewScore = targetAtHit.CalculateScore(correctedHit, previewCaliberMm);
                }

                var recordedHits = !showWeaponOffsets && !_shootingSession.IsMonoscope && ShootingSettings.ShowAllHits
                    ? _recording.GetNormalizedHitRecordList()
                    : null;
                var targetAimPath = !showWeaponOffsets && aimPath.Length > 0 && lastTargetHit is not null
                    ? _recording.GetLastHitRecord()?.AimPathBefore
                    : null;
                Func<double, double, Position?>? mapFollowPoint = null;
                if (!showWeaponOffsets && followPath.Length > 1 && lastTargetHit is not null && lastHit is { } hitWithTrace)
                {
                    double offsetX = hitWithTrace.X - hitWithTrace.LaserPosition.X;
                    double offsetY = hitWithTrace.Y - hitWithTrace.LaserPosition.Y;
                    mapFollowPoint = (x, y) => calibration.MapHit((x + offsetX) / pixels.Width, (y + offsetY) / pixels.Height);
                }
                TargetRenderer.DrawTargetPreview(target, lastTargetHit, calibration.Settings,
                    previewCaliberMm, recordedHits, IsDarkTheme, originalTargetHit, aimPath: targetAimPath,
                    followPath: followPath, mapFollowPoint: mapFollowPoint, liveAim: liveAim, liveOffsetAim: liveOffsetAim);
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

            await CameraPreview.ShowFrameAsync(pixels, measuredFps, cancellationToken,
                guidance: AudioSettings.GetGuidancePreview());
        }
    }
}
