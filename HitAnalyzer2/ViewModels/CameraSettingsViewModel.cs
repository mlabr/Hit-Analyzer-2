using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Target;
using HitAnalyzer2.Services.Video;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HitAnalyzer2.ViewModels
{
    public sealed record CameraFlipOption(string Label, FlipMode? Mode)
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

        internal CameraSettingsViewModel(SettingsService settings, CameraCaptureSession captureSession,
            TargetCalibrationService calibration)
        {
            _settings = settings;
            _captureSession = captureSession;
            Calibration = calibration;
            selectedCameraFlip = CameraFlipOptions.First(option => option.Mode == settings.Current.Camera.Flip);
        }

        [ObservableProperty]
        private IReadOnlyList<CameraDevice> cameras = Array.Empty<CameraDevice>();

        [ObservableProperty]
        private CameraDevice? selectedCamera;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSelectCameraMode))]
        private IReadOnlyList<CameraMode> cameraModes = Array.Empty<CameraMode>();

        [ObservableProperty]
        private CameraMode? selectedCameraMode;

        [ObservableProperty]
        private CameraFlipOption selectedCameraFlip;

        internal void SetCalibrationCorners(CalibrationCorners corners)
        {
            Calibration.SetCorners(corners);
        }

        internal void MoveCalibrationCorner(int index, Position point)
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
        private string cameraModePlaceholder = "Select a camera";

        public bool CanSelectCameraMode => !IsDiscoveringCameras && CameraModes.Count > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSelectCameraMode))]
        private bool isDiscoveringCameras;

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

        internal Task StopCameraAsync() => _captureSession.StopAsync();

        internal CameraSettings GetConfiguration(CameraSettings current) =>
            !IsDiscoveringCameras && SelectedCamera is { } camera
                ? current with
                {
                    DeviceId = camera.DeviceId,
                    DeviceIndex = camera.DeviceId is null ? camera.Index : null,
                    Mode = SelectedCameraMode,
                    Flip = SelectedCameraFlip.Mode
                }
                : current;

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
            Calibration.SetCorners(null);
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
                    null => "Select a camera",
                    { ModeDetectionError: not null } => "Could not load camera modes",
                    _ when !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() => "Camera mode detection is available on Windows and Linux",
                    _ when CameraModes.Count == 0 => "No camera modes at 640 × 480 or higher and 25 FPS or faster",
                    _ => "Select a camera mode"
                };
            }
            finally
            {
                _updatingModes = false;
            }
        }
    }
}
