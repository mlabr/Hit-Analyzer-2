using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using HitAnalyzer2.Services.Detection;
using HitAnalyzer2.Repository;
using HitAnalyzer2.Services.Recording;
using HitAnalyzer2.Services.Shooting;
using HitAnalyzer2.Services.Target;
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
                var weaponsDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Weapons");
                var weaponProfiles = new WeaponFileRepository(weaponsDirectory);
                var settings = new SettingsService(GetSettingsPath(AppContext.BaseDirectory), weaponProfiles);
                ApplyTheme(settings.Current.Theme);
                var detection = settings.Current.Detection;
                ILaserDetector CreateDetector(DetectionMethod method, int sensitivity)
                {
                    int threshold = BrightPixelFilter.ThresholdFromSensitivity(sensitivity);
                    return method == DetectionMethod.Simple
                        ? new LaserDetectorSimple(threshold)
                        : new LaserDetectorBlob(threshold, detection.MinimumHitAreaPixels ?? 1);
                }

                var aimPositionHandler = new AimPositionHandler(settings.Current.Shooting);
                var weaponHandler = new WeaponHandler(settings.InitialWeapon, aimPositionHandler);
                var cameraCalibration = new TargetCalibrationService(settings.Current.Target.Calibration);
                var targetFiles = new TargetFileRepository(Path.Combine(AppContext.BaseDirectory, "Data", "Targets"));
                var targetSettings = new TargetSettingsViewModel(targetFiles, cameraCalibration,
                    settings.Current.Target.SelectedTargetFileName, settings.Current.Target);
                var recording = new RecordingService(cameraCalibration);
                var shootingSession = new ShootingSessionService(
                    CreateDetector(detection.Method, detection.DetectionSensitivity), aimPositionHandler, weaponHandler,
                    settings.Current.Shooting, detection, cameraCalibration, recording, Program.StartedAt);
                var detectionSettings = new DetectionSettingsViewModel(settings,
                    (method, sensitivity) => shootingSession.SetLaserDetector(CreateDetector(method, sensitivity)));
                var weaponSettings = new WeaponSettingsViewModel(settings, shootingSession, weaponHandler, weaponProfiles);
                var mainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(settings, detectionSettings, weaponSettings,
                        targetSettings, shootingSession, cameraCalibration, recording),
                };
                var externalTriggerHandler = new ExternalTriggerHandler(mainWindow);
                externalTriggerHandler.PressChanged += shootingSession.OnExternalTriggerChanged;
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        internal static void ApplyTheme(ThemePreference theme)
        {
            if (Current is { } application)
                application.RequestedThemeVariant = theme switch
                {
                    ThemePreference.Light => ThemeVariant.Light,
                    ThemePreference.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default
                };
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
