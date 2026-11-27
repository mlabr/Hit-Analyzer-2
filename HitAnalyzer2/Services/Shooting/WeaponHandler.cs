using System;
using System.Threading;
using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Detection;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed record ActiveWeapon(string? FileName, WeaponSettings Settings, decimal CaliberDiameterMm)
    {
        internal string? Name => FileName is null ? null : (Settings as WeaponProfileModel)?.Name ?? FileName;

        internal AimPosition GetAimPosition(LaserPosition position) =>
            new(position.X + Settings.OffsetX, position.Y + Settings.OffsetY, position);

        internal SavedWeaponProfile? GetSelectedProfile() =>
            FileName is { } fileName && Settings is WeaponProfileModel profile
                ? new(fileName, profile with { Caliber = CaliberDiameterMm })
                : null;
    }

    internal sealed class WeaponHandler
    {
        internal const int MinimumOffset = -100;
        internal const int MaximumOffset = 100;

        private readonly object _gate = new();
        private ActiveWeapon _current;

        internal WeaponHandler(WeaponSettings settings) => _current = CreateSnapshot(null, settings);

        internal ActiveWeapon Current => Volatile.Read(ref _current);

        internal void Select(string? fileName, WeaponProfileModel? profile)
        {
            lock (_gate)
                Volatile.Write(ref _current, profile is null
                    ? _current with { FileName = null, CaliberDiameterMm = 0 }
                    : CreateSnapshot(fileName, profile));
        }

        internal void SetCaliber(decimal diameterMm)
        {
            lock (_gate)
                Volatile.Write(ref _current, _current with { CaliberDiameterMm = Math.Clamp(diameterMm, 0, 20) });
        }

        internal void SetOffset(int x, int y) =>
            UpdateSettings(settings => settings with { OffsetX = x, OffsetY = y });

        internal void SetTrigger(TriggerTypeEnum trigger) =>
            UpdateSettings(settings => settings with { Trigger = trigger });

        internal void SetDetectionInterval(int interval) =>
            UpdateSettings(settings => settings with { NexDetectionInterval = ClampInterval(interval) });

        private void UpdateSettings(Func<WeaponSettings, WeaponSettings> update)
        {
            lock (_gate)
                Volatile.Write(ref _current, _current with { Settings = update(_current.Settings) });
        }

        private static ActiveWeapon CreateSnapshot(string? fileName, WeaponSettings settings) =>
            new(fileName, settings with { NexDetectionInterval = ClampInterval(settings.NexDetectionInterval) },
                Math.Clamp((settings as WeaponProfileModel)?.Caliber ?? 0, 0, 20));

        private static int ClampInterval(int value) => Math.Clamp(value,
            ShootingSessionService.NextDetectionMinimumInterval, ShootingSessionService.NextDetectionMaximumInterval);
    }
}
