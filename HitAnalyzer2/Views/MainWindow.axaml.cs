using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Styling;
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
        private Point? _zoomPointer;

        public MainWindow()
        {
            InitializeComponent();
            CameraPreview.Margin = PreviewMargin;
            PreviewImage.PropertyChanged += OnPreviewImageChanged;
            CameraPreview.AddHandler(PointerMovedEvent, UpdateZoomPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
            CameraPreview.PointerExited += (_, _) =>
            {
                _zoomPointer = null;
                UpdateCalibrationZoom();
            };
            TargetSettings.ZoomSelector.SelectionChanged += (_, _) => UpdateCalibrationZoom();
            TargetSettings.ResetCalibrationButton.Click += (_, _) => CalibrationOverlay.ResetToSquare();
            DataContextChanged += (_, _) =>
            {
                if (_cameraSettingsViewModel is { } previous)
                    previous.Calibration.CalibrationGeometryChanged -= InvalidateCalibrationOverlay;
                _cameraSettingsViewModel = (DataContext as MainWindowViewModel)?.CameraSettings;
                if (_cameraSettingsViewModel is { } current)
                    current.Calibration.CalibrationGeometryChanged += InvalidateCalibrationOverlay;
                UpdateCalibrationOverlay();
                UpdatePreviewTheme();
            };
            ScalingChanged += (_, _) => FitWindowToPreview();
            PropertyChanged += (_, e) =>
            {
                if (e.Property == ActualThemeVariantProperty)
                    UpdatePreviewTheme();
                if (e.Property == WindowStateProperty && WindowState == WindowState.Normal)
                    FitWindowToPreview();
            };
            Opened += async (_, _) =>
            {
                if (DataContext is MainWindowViewModel viewModel)
                    await viewModel.RefreshCamerasAsync();
            };
            Closing += StopCameraBeforeClosing;
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
                {
                    ShowHelp(this, e);
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
        }

        private void ShowShootingSettings(object? sender, RoutedEventArgs e) => ShowSettings(ShootingSettings);

        private void UpdatePreviewTheme()
        {
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.IsDarkTheme = ActualThemeVariant == ThemeVariant.Dark;
        }

        private void ShowCameraSettings(object? sender, RoutedEventArgs e) => ShowSettings(CameraSettings);

        private void ShowDetectionSettings(object? sender, RoutedEventArgs e) => ShowSettings(DetectionSettings);

        private void ShowTargetSettings(object? sender, RoutedEventArgs e) =>
            ShowSettings(TargetSettings);

        private void ShowWeaponSettings(object? sender, RoutedEventArgs e) =>
            ShowSettings(WeaponSettings);

        private void ShowAbout(object? sender, RoutedEventArgs e)
        {
            ShowSettings(About);
            AboutContent.IsVisible = true;
            HelpContent.IsVisible = false;
        }

        private void ShowHelp(object? sender, RoutedEventArgs e)
        {
            ShowSettings(About);
            AboutContent.IsVisible = false;
            HelpContent.IsVisible = true;
        }

        private void ShowHtmlHelp(object? sender, RoutedEventArgs e)
        {
            string helpPath = Path.Combine(AppContext.BaseDirectory, "Data", "Help", "index.html");
            try
            {
                if (!File.Exists(helpPath))
                    throw new FileNotFoundException("The HTML help file is missing.", helpPath);

                using var browser = Process.Start(new ProcessStartInfo(helpPath) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                if (DataContext is MainWindowViewModel viewModel)
                    viewModel.ConfigurationMessage = $"Could not open HTML help. {ex.Message}";
            }
        }

        private void ShowSettings(Control view)
        {
			ShootingSettings.IsVisible = view == ShootingSettings;
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.IsTargetPreviewActive = view == ShootingSettings || view == WeaponSettings;
			CameraSettings.IsVisible = view == CameraSettings;
            DetectionSettings.IsVisible = view == DetectionSettings;
            WeaponSettings.IsVisible = view == WeaponSettings;
            
            TargetSettings.IsVisible = view == TargetSettings;
            About.IsVisible = view == About;
            InformationPanel.IsVisible = About.IsVisible;
            CameraPreview.IsVisible = !About.IsVisible;
            HitCounter.IsVisible = !About.IsVisible;
            if (About.IsVisible)
                MinWidth = 450;
            _zoomPointer = null;
            UpdateCalibrationZoom();
            UpdateCalibrationOverlay();
            FitWindowToPreview();
        }

        private void InvalidateCalibrationOverlay()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                CalibrationOverlay.InvalidateVisual();
                UpdateCalibrationZoom();
            }
            else
                Dispatcher.UIThread.Post(InvalidateCalibrationOverlay);
        }

        private void UpdateZoomPointer(object? sender, PointerEventArgs e)
        {
            _zoomPointer = e.GetPosition(PreviewImage);
            UpdateCalibrationZoom();
        }

        private void UpdateCalibrationZoom()
        {
            Bitmap? source = null;
            Point imagePoint = default;
            if (TargetSettings.IsVisible && _zoomPointer is { } pointer &&
                PreviewImage.Source is Bitmap bitmap)
            {
                var bounds = PreviewImage.Bounds;
                var size = Stretch.Uniform.CalculateSize(bounds.Size, bitmap.Size, StretchDirection.DownOnly);
                var imageBounds = new Rect((bounds.Width - size.Width) / 2,
                    (bounds.Height - size.Height) / 2, size.Width, size.Height);
                if (size.Width > 0 && size.Height > 0 && imageBounds.Contains(pointer))
                {
                    source = bitmap;
                    imagePoint = new Point((pointer.X - imageBounds.X) * bitmap.Size.Width / size.Width,
                        (pointer.Y - imageBounds.Y) * bitmap.Size.Height / size.Height);
                }
            }

            TargetSettings.ZoomPreview.Update(source, imagePoint, 2 << Math.Clamp(TargetSettings.ZoomSelector.SelectedIndex, 0, 2),
                source is null ? null : _cameraSettingsViewModel);
        }

        private void UpdateCalibrationOverlay()
        {
            if (!TargetSettings.IsVisible || PreviewImage.Source is not Bitmap bitmap)
            {
                CalibrationOverlay.Update(null, default);
                TargetSettings.ResetCalibrationButton.IsEnabled = false;
                return;
            }

            var bounds = PreviewImage.Bounds;
            var size = Stretch.Uniform.CalculateSize(bounds.Size, bitmap.Size, StretchDirection.DownOnly);
            var imageBounds = new Rect(bounds.X + (bounds.Width - size.Width) / 2,
                bounds.Y + (bounds.Height - size.Height) / 2, size.Width, size.Height);
            CalibrationOverlay.Update(_cameraSettingsViewModel, imageBounds);
            TargetSettings.ResetCalibrationButton.IsEnabled = CalibrationOverlay.IsVisible;
        }

        private void OnPreviewImageChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == BoundsProperty)
            {
                UpdateCalibrationOverlay();
                UpdateCalibrationZoom();
                return;
            }

            if (e.Property != Image.SourceProperty)
                return;

            UpdateCalibrationZoom();
            var pixelSize = (PreviewImage.Source as Bitmap)?.PixelSize;
            if (pixelSize == _previewPixelSize)
                return;

            _previewPixelSize = pixelSize;
            UpdateCalibrationOverlay();
            FitWindowToPreview();
        }

        private void FitWindowToPreview()
        {
            if (About.IsVisible || _previewPixelSize is not { } pixels || WindowState != WindowState.Normal || _stoppingCamera)
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

            double previewWidth = Math.Ceiling(imageSize.Width * scale + horizontalSpace);
            double statisticsWidth = Math.Ceiling(SettingsPanel.Width + HitCounter.DesiredSize.Width + 20);
            MinWidth = DataContext is MainWindowViewModel { IsTargetPreviewActive: true }
                ? Math.Min(maxWidth, Math.Max(450, Math.Max(previewWidth + 20, statisticsWidth)))
                : 450;
            Width = Math.Min(maxWidth, Math.Max(MinWidth, previewWidth));
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
