using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HitAnalyzer2.ViewModels;
using System;

namespace HitAnalyzer2.Views
{
    public partial class MainWindow : Window
    {
        private static readonly Thickness PreviewMargin = new(12);
        private PixelSize? _previewPixelSize;
        private bool _stoppingCamera;
        private bool _cameraStopped;

        public MainWindow()
        {
            InitializeComponent();
            CameraPreview.Margin = PreviewMargin;
            PreviewImage.PropertyChanged += OnPreviewImageChanged;
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
            ShowSettings(SettingsPlaceholder, "Nastavení terče zatím není k dispozici.");

        private void ShowWeaponSettings(object? sender, RoutedEventArgs e) =>
            ShowSettings(WeaponSettings);

        private void ShowSettings(Control view, string? placeholder = null)
        {
			ShootingSettings.IsVisible = view == ShootingSettings;
			CameraSettings.IsVisible = view == CameraSettings;
            DetectionSettings.IsVisible = view == DetectionSettings;
            WeaponSettings.IsVisible = view == WeaponSettings;
            
            SettingsPlaceholder.Text = placeholder;
            SettingsPlaceholder.IsVisible = view == SettingsPlaceholder;
        }

        private void OnPreviewImageChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Image.SourceProperty || PreviewImage.Source is not Bitmap bitmap ||
                bitmap.PixelSize == _previewPixelSize)
                return;

            _previewPixelSize = bitmap.PixelSize;
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
            double verticalSpace = verticalMargin + SettingsMenu.DesiredSize.Height;
            double scale = Math.Clamp(Math.Min(
                (maxWidth - horizontalSpace) / imageSize.Width,
                (maxHeight - verticalSpace) / imageSize.Height), 0, 1);

            Width = Math.Min(maxWidth, Math.Max(MinWidth, Math.Ceiling(imageSize.Width * scale + horizontalSpace)));
            Height = Math.Min(maxHeight, Math.Ceiling(imageSize.Height * scale + verticalSpace));

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
