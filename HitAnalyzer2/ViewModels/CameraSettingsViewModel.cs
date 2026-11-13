using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Services.Target;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HitAnalyzer2.ViewModels
{
    public sealed record CameraFlipOption(string Label, FlipMode? Mode)
    {
        public override string ToString() => Label;
    }

    public sealed record CameraCalibrationOption(string Label, CameraCalibrationMode Mode)
    {
        public override string ToString() => Label;
    }

    public partial class CameraSettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _settings;
        private readonly CameraCaptureSession _captureSession;
        internal TargetCalibrationService Calibration { get; }
        private bool _updatingModes;

        public IReadOnlyList<CameraFlipOption> CameraFlipOptions { get; } = Array.AsReadOnly(new CameraFlipOption[]
        {
            new("No Flip", null),
            new("Flip Horizontal", FlipMode.Y),
            new("Flip Vertical", FlipMode.X),
            new("Flip Vertical & Horizontal", FlipMode.XY)
        });

        public IReadOnlyList<CameraCalibrationOption> CalibrationModes { get; } = Array.AsReadOnly(
            new CameraCalibrationOption[] { new("None", CameraCalibrationMode.None), new("Hit only", CameraCalibrationMode.HitOnly) });

        internal CameraSettingsViewModel(SettingsService settings, CameraCaptureSession captureSession,
            TargetCalibrationService calibration)
        {
            _settings = settings;
            _captureSession = captureSession;
            Calibration = calibration;
            showAimPointPreview = settings.Current.Detection.ShowAimPointPreview;
            selectedCalibrationMode = CalibrationModes.First(option => option.Mode == calibration.Settings.Mode);
            selectedCameraFlip = CameraFlipOptions.First(option => option.Mode == settings.Current.Camera.Flip);
            SettingsMessage = settings.LoadError;
        }

        [ObservableProperty]
        private IReadOnlyList<CameraDevice> cameras = Array.Empty<CameraDevice>();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCameraSettingsCommand))]
        private CameraDevice? selectedCamera;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSelectCameraMode))]
        private IReadOnlyList<CameraMode> cameraModes = Array.Empty<CameraMode>();

        [ObservableProperty]
        private CameraMode? selectedCameraMode;

        [ObservableProperty]
        private CameraFlipOption selectedCameraFlip;

        [ObservableProperty]
        private CameraCalibrationOption selectedCalibrationMode;

        [ObservableProperty]
        private bool showAimPointPreview;

        [ObservableProperty]
        private string? targetSettingsMessage;

        partial void OnShowAimPointPreviewChanged(bool value) => TargetSettingsMessage = null;

        partial void OnSelectedCalibrationModeChanged(CameraCalibrationOption value)
        {
            Calibration.Update(Calibration.Settings with { Mode = value.Mode });
            TargetSettingsMessage = null;
        }

        internal void SetCalibrationSquare(double width, double height)
            => SetCalibrationCorners(CalibrationCorners.CenteredSquare(width, height));

        internal void SetCalibrationCorners(CalibrationCorners corners)
        {
            Calibration.Update(Calibration.Settings with { Corners = corners });
            TargetSettingsMessage = null;
        }

        internal void MoveCalibrationCorner(int index, CalibrationPoint point)
        {
            var settings = Calibration.Settings;
            if (settings.Corners is not { } corners)
                return;
            SetCalibrationCorners(index switch
            {
                0 => corners with { TopLeft = point },
                1 => corners with { TopRight = point },
                2 => corners with { BottomRight = point },
                3 => corners with { BottomLeft = point },
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            });
        }

        [ObservableProperty]
        private string cameraModePlaceholder = "Vyberte kameru";

        public bool CanSelectCameraMode => !IsDiscoveringCameras && CameraModes.Count > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSelectCameraMode))]
        [NotifyCanExecuteChangedFor(nameof(SaveCameraSettingsCommand))]
        private bool isDiscoveringCameras;

        [ObservableProperty]
        private string? settingsMessage;

        [RelayCommand]
        internal async Task RefreshCamerasAsync()
        {
            if (_captureSession.IsClosing || IsDiscoveringCameras)
                return;

            IsDiscoveringCameras = true;
            try
            {
                await _captureSession.RefreshAsync(ApplyDetectedCameras);
            }
            finally
            {
                IsDiscoveringCameras = false;
            }
        }

        private (CameraDevice? Camera, CameraMode? Mode, FlipMode? Flip) ApplyDetectedCameras(
            IReadOnlyList<CameraDevice> detected)
        {
            var previousCamera = SelectedCamera;
            var previousMode = SelectedCameraMode;
            Cameras = detected;

            var saved = _settings.Current.Camera;
            var sameCamera = detected.FirstOrDefault(camera => previousCamera is not null &&
                (previousCamera.DeviceId is { } id ? camera.DeviceId == id : camera.Index == previousCamera.Index));
            var savedCamera = detected.FirstOrDefault(camera => saved.DeviceId is { } id
                ? camera.DeviceId == id
                : camera.DeviceId is null && camera.Index == saved.DeviceIndex);

            SelectedCamera = sameCamera ?? savedCamera ?? detected.FirstOrDefault();
            UpdateCameraModes(sameCamera is not null ? previousMode : savedCamera is not null ? saved.Mode : null);
            if (sameCamera is null && (savedCamera is null || SelectedCameraMode != saved.Mode))
                ResetCalibration();
            return (SelectedCamera, SelectedCameraMode, SelectedCameraFlip.Mode);
        }

        internal async Task StopCameraAsync()
        {
            var stopping = _captureSession.StopAsync();
            SaveCameraSettingsCommand.NotifyCanExecuteChanged();
            await stopping;
        }

        private bool CanSaveCameraSettings() => !_captureSession.IsClosing && !IsDiscoveringCameras && SelectedCamera is not null;

        [RelayCommand(CanExecute = nameof(CanSaveCameraSettings))]
        private void SaveCameraSettings()
        {
            if (SelectedCamera is not { } camera)
                return;

            try
            {
                _settings.SaveCamera(camera, SelectedCameraMode, SelectedCameraFlip.Mode);
                SettingsMessage = "Nastavení kamery uloženo.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                SettingsMessage = $"Nastavení kamery se nepodařilo uložit. {ex.Message}";
            }
        }

        [RelayCommand]
        private void SaveTargetSettings()
        {
            try
            {
                if (!IsDiscoveringCameras && SelectedCamera is { } camera)
                {
                    _settings.SaveCamera(camera, SelectedCameraMode, SelectedCameraFlip.Mode,
                        ShowAimPointPreview, Calibration.Settings);
                    TargetSettingsMessage = "Nastavení terče uloženo.";
                }
                else
                {
                    _settings.SaveAimPointPreview(ShowAimPointPreview);
                    TargetSettingsMessage = "Volba náhledu uložena. Kalibraci uložte po výběru kamery.";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                TargetSettingsMessage = $"Nastavení terče se nepodařilo uložit. {ex.Message}";
            }
        }

        partial void OnSelectedCameraChanged(CameraDevice? value)
        {
            if (!_captureSession.IsClosing && !IsDiscoveringCameras)
            {
                UpdateCameraModes(null);
                SwitchToSelectedCamera();
            }
        }

        partial void OnSelectedCameraModeChanged(CameraMode? value)
        {
            if (!_captureSession.IsClosing && !IsDiscoveringCameras && !_updatingModes)
                SwitchToSelectedCamera();
        }

        partial void OnSelectedCameraFlipChanged(CameraFlipOption value)
        {
            if (!_captureSession.IsClosing && !IsDiscoveringCameras && SelectedCamera is not null)
                SwitchToSelectedCamera();
        }

        private void SwitchToSelectedCamera()
        {
            ResetCalibration();
            _ = _captureSession.SwitchAsync(SelectedCamera, SelectedCameraMode, SelectedCameraFlip.Mode);
        }

        private void ResetCalibration()
        {
            SelectedCalibrationMode = CalibrationModes[0];
            Calibration.Update(new CameraCalibrationSettings());
        }

        private void UpdateCameraModes(CameraMode? preferredMode)
        {
            _updatingModes = true;
            try
            {
                var camera = SelectedCamera;
                CameraModes = camera?.Modes ?? Array.Empty<CameraMode>();
                SelectedCameraMode = CameraModes.FirstOrDefault(mode => mode == preferredMode)
                    ?? CameraModes.FirstOrDefault();
                CameraModePlaceholder = camera switch
                {
                    null => "Vyberte kameru",
                    { ModeDetectionError: not null } => "Režimy se nepodařilo načíst",
                    _ when !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() => "Detekce režimů je dostupná ve Windows a Linuxu",
                    _ when CameraModes.Count == 0 => "Žádný režim od 640 × 480 a 25 FPS",
                    _ => "Vyberte režim"
                };
            }
            finally
            {
                _updatingModes = false;
            }
        }
    }
}
