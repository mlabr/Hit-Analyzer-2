using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HitAnalyzer2.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace HitAnalyzer2.Views
{
    public partial class MainWindow : Window
    {
        private static readonly Thickness PreviewMargin = new(12);
        private PixelSize? _previewPixelSize;
        private bool _stoppingCamera;
        private bool _cameraStopped;
        private CameraSettingsViewModel? _cameraSettingsViewModel;

        public MainWindow()
        {
            InitializeComponent();
            CameraPreview.Margin = PreviewMargin;
            PreviewImage.PropertyChanged += OnPreviewImageChanged;
            CameraSettings.ResetCalibrationButton.Click += (_, _) => CalibrationOverlay.ResetToSquare();
            DataContextChanged += (_, _) =>
            {
                if (_cameraSettingsViewModel is { } previous)
                    previous.PropertyChanged -= OnCameraSettingsChanged;
                _cameraSettingsViewModel = (DataContext as MainWindowViewModel)?.CameraSettings;
                if (_cameraSettingsViewModel is { } current)
                    current.PropertyChanged += OnCameraSettingsChanged;
                UpdateCalibrationOverlay();
            };
            ScalingChanged += (_, _) => FitWindowToPreview();
            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty && WindowState == WindowState.Normal)
                    FitWindowToPreview();
            };
            Opened += async (_, _) =>
            {
                if (DataContext is MainWindowViewModel viewModel)
                    await viewModel.RefreshCamerasAsync();
            };
            Closing += StopCameraBeforeClosing;
        }

        private void ShowShootingSettings(object? sender, RoutedEventArgs e) => ShowSettings(ShootingSettings);

        private void ShowCameraSettings(object? sender, RoutedEventArgs e) => ShowSettings(CameraSettings);

        private void ShowDetectionSettings(object? sender, RoutedEventArgs e) => ShowSettings(DetectionSettings);

        private void ShowTargetSettings(object? sender, RoutedEventArgs e) =>
            ShowSettings(SettingsPlaceholder, "Target settings not implementet yet.");

        private void ShowWeaponSettings(object? sender, RoutedEventArgs e) =>
            ShowSettings(WeaponSettings);

        private async void ShowHelp(object? sender, RoutedEventArgs e)
        {
            string helpPath = Path.Combine(AppContext.BaseDirectory, "Data", "Help", "index.html");
            string message;
            if (File.Exists(helpPath))
            {
                try
                {
                    using var browser = Process.Start(new ProcessStartInfo(helpPath) { UseShellExecute = true });
                    return;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
                {
                    message = "Could not open the help page. Check that a default web browser is configured.";
                }
            }
            else
            {
                message = "The help file is missing. Check that Data/Help/index.html is in the application folder.";
            }

            var closeButton = new Button { Content = "OK", IsDefault = true, IsCancel = true,
                HorizontalAlignment = HorizontalAlignment.Right };
            var dialog = new Window
            {
                Title = "Hit Analyzer 2 Help",
                Width = 380,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        closeButton
                    }
                }
            };
            closeButton.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(this);
        }

        private void ShowSettings(Control view, string? placeholder = null)
        {
			ShootingSettings.IsVisible = view == ShootingSettings;
			CameraSettings.IsVisible = view == CameraSettings;
            DetectionSettings.IsVisible = view == DetectionSettings;
            WeaponSettings.IsVisible = view == WeaponSettings;
            
            SettingsPlaceholder.Text = placeholder;
            SettingsPlaceholder.IsVisible = view == SettingsPlaceholder;
            UpdateCalibrationOverlay();
        }

        private void OnCameraSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CameraSettingsViewModel.SelectedCalibrationMode))
                UpdateCalibrationOverlay();
        }

        private void UpdateCalibrationOverlay()
        {
            if (!CameraSettings.IsVisible || PreviewImage.Source is not Bitmap bitmap)
            {
                CalibrationOverlay.Update(null, default);
                CameraSettings.ResetCalibrationButton.IsEnabled = false;
                return;
            }

            var bounds = PreviewImage.Bounds;
            var size = Stretch.Uniform.CalculateSize(bounds.Size, bitmap.Size, StretchDirection.DownOnly);
            var imageBounds = new Rect(bounds.X + (bounds.Width - size.Width) / 2,
                bounds.Y + (bounds.Height - size.Height) / 2, size.Width, size.Height);
            CalibrationOverlay.Update(_cameraSettingsViewModel, imageBounds);
            CameraSettings.ResetCalibrationButton.IsEnabled = CalibrationOverlay.IsVisible;
        }

        private void OnPreviewImageChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == BoundsProperty)
            {
                UpdateCalibrationOverlay();
                return;
            }

            if (e.Property != Image.SourceProperty)
                return;

            var pixelSize = (PreviewImage.Source as Bitmap)?.PixelSize;
            if (pixelSize == _previewPixelSize)
                return;

            _previewPixelSize = pixelSize;
            UpdateCalibrationOverlay();
            FitWindowToPreview();
        }

        private void FitWindowToPreview()
        {
            if (_previewPixelSize is not { } pixels || WindowState != WindowState.Normal || _stoppingCamera)
                return;

            var imageSize = new Size(pixels.Width / RenderScaling, pixels.Height / RenderScaling);
            double horizontalSpace = PreviewMargin.Left + PreviewMargin.Right + SettingsPanel.Width;
            double verticalMargin = PreviewMargin.Top + PreviewMargin.Bottom;
            var frame = FrameSize ?? ClientSize;
            double frameWidth = Math.Max(0, frame.Width - ClientSize.Width);
            double frameHeight = Math.Max(0, frame.Height - ClientSize.Height);
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            double maxWidth = screen is null ? double.PositiveInfinity
                : Math.Max(1, screen.WorkingArea.Width / DesktopScaling - frameWidth);
            double maxHeight = screen is null ? double.PositiveInfinity
                : Math.Max(1, screen.WorkingArea.Height / DesktopScaling - frameHeight);

            SettingsMenu.Measure(Size.Infinity);
            HitCounter.Measure(Size.Infinity);
            double verticalSpace = verticalMargin + SettingsMenu.DesiredSize.Height + HitCounter.DesiredSize.Height;
            double scale = Math.Clamp(Math.Min(
                (maxWidth - horizontalSpace) / imageSize.Width,
                (maxHeight - verticalSpace) / imageSize.Height), 0, 1);

            Width = Math.Min(maxWidth, Math.Max(MinWidth, Math.Ceiling(imageSize.Width * scale + horizontalSpace)));
            Height = Math.Min(maxHeight, Math.Max(MinHeight, Math.Ceiling(imageSize.Height * scale + verticalSpace)));

            if (screen is { } currentScreen)
            {
                var work = currentScreen.WorkingArea;
                int outerWidth = (int)Math.Ceiling((Width + frameWidth) * DesktopScaling);
                int outerHeight = (int)Math.Ceiling((Height + frameHeight) * DesktopScaling);
                var position = new PixelPoint(
                    Math.Clamp(Position.X, work.X, Math.Max(work.X, work.Right - outerWidth)),
                    Math.Clamp(Position.Y, work.Y, Math.Max(work.Y, work.Bottom - outerHeight)));
                if (position != Position)
                    Position = position;
            }
        }

        private async void StopCameraBeforeClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_cameraStopped || DataContext is not MainWindowViewModel viewModel)
                return;

            e.Cancel = true;
            if (_stoppingCamera)
                return;

            _stoppingCamera = true;
            await viewModel.StopCameraAsync();
            _cameraStopped = true;
            Dispatcher.UIThread.Post(Close);
        }
    }
}
