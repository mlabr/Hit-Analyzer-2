using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public partial class CameraSettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _settings;
        private readonly CameraCaptureSession _captureSession;
        private bool _updatingModes;
        private volatile int _cameraBrightnessPercent;
        private volatile int _cameraContrastPercent;
        private volatile int _cameraSaturationPercent;

        public IReadOnlyList<CameraFlipOption> CameraFlipOptions { get; } = Array.AsReadOnly(new CameraFlipOption[]
        {
            new("No Flip", null),
            new("Flip Horizontal", FlipMode.Y),
            new("Flip Vertical", FlipMode.X),
            new("Flip Vertical & Horizontal", FlipMode.XY)
        });

        internal CameraSettingsViewModel(SettingsService settings, CameraCaptureSession captureSession)
        {
            _settings = settings;
            _captureSession = captureSession;
            selectedCameraFlip = CameraFlipOptions.First(option => option.Mode == settings.Current.Camera.Flip);
            _cameraBrightnessPercent = settings.Current.Camera.BrightnessPercent;
            _cameraContrastPercent = settings.Current.Camera.ContrastPercent;
            _cameraSaturationPercent = settings.Current.Camera.SaturationPercent;
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

        public int CameraBrightnessPercent
        {
            get => _cameraBrightnessPercent;
            set
            {
                int percent = Math.Clamp(value, 0, 100);
                if (percent == _cameraBrightnessPercent)
                    return;

                _cameraBrightnessPercent = percent;
                OnPropertyChanged(nameof(CameraBrightnessPercent));
            }
        }

        public int CameraContrastPercent
        {
            get => _cameraContrastPercent;
            set
            {
                int percent = Math.Clamp(value, 0, 100);
                if (percent == _cameraContrastPercent)
                    return;

                _cameraContrastPercent = percent;
                OnPropertyChanged(nameof(CameraContrastPercent));
            }
        }

        public int CameraSaturationPercent
        {
            get => _cameraSaturationPercent;
            set
            {
                int percent = Math.Clamp(value, 0, 100);
                if (percent == _cameraSaturationPercent)
                    return;

                _cameraSaturationPercent = percent;
                OnPropertyChanged(nameof(CameraSaturationPercent));
            }
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
                _settings.SaveCamera(camera, SelectedCameraMode, SelectedCameraFlip.Mode,
                    CameraBrightnessPercent, CameraContrastPercent, CameraSaturationPercent);
                SettingsMessage = "Nastavení kamery uloženo.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                SettingsMessage = $"Nastavení kamery se nepodařilo uložit. {ex.Message}";
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

        private void SwitchToSelectedCamera() =>
            _ = _captureSession.SwitchAsync(SelectedCamera, SelectedCameraMode, SelectedCameraFlip.Mode);

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
