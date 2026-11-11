using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.ViewModels;
using HitAnalyzer2.Views;
using System;
using System.IO;

namespace HitAnalyzer2
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var settings = new SettingsService(GetSettingsPath(AppContext.BaseDirectory));
                var detection = settings.Current.Detection;
                ILaserDetector CreateDetector(DetectionMethod method, int sensitivity)
                {
                    int threshold = LaserIntensity.ThresholdFromSensitivity(sensitivity);
                    return method == DetectionMethod.Simple
                        ? new LaserDetectorSimple(threshold)
                        : new LaserDetectorBlob(threshold, detection.MinimumHitAreaPixels ?? 1);
                }

                var shootingSession = new ShootingSessionService(
                    CreateDetector(detection.Method, detection.DetectionSensitivity), Program.StartedAt);
                var detectionSettings = new DetectionSettingsViewModel(settings,
                    (method, sensitivity) => shootingSession.SetLaserDetector(CreateDetector(method, sensitivity)));
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(settings, detectionSettings, shootingSession.ProcessFrame),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static string GetSettingsPath(string baseDirectory)
        {
            for (DirectoryInfo? directory = new(baseDirectory); directory is not null; directory = directory.Parent)
            {
                // The publish profiles put their output under bin/.../publish.
                if (directory.Name.Equals("publish", StringComparison.OrdinalIgnoreCase))
                    break;

                if (File.Exists(Path.Combine(directory.FullName, "HitAnalyzer2.csproj")))
                    return Path.Combine(directory.FullName, "settings.json");
            }

            return Path.Combine(baseDirectory, "settings.json");
        }
    }
}
