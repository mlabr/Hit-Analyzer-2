using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class WeaponHandler
    {
        internal const int MinimumOffset = -100;
        internal const int MaximumOffset = 100;

        private readonly SettingsService _settings;
        private readonly AimPositionHandler _aimPositionHandler;

        internal WeaponHandler(SettingsService settings, AimPositionHandler aimPositionHandler)
        {
            _settings = settings;
            _aimPositionHandler = aimPositionHandler;
            var weapon = settings.InitialWeapon;
            SetOffset(weapon.OffsetX, weapon.OffsetY);
        }

        internal (int X, int Y) Offset { get; private set; }

        internal IReadOnlyList<WeaponOption> LoadWeapons(out string? loadError)
        {
            loadError = null;
            var weapons = new List<WeaponOption>();
            try
            {
                var paths = Directory.GetFiles(_settings.WeaponsDirectory, "*.json");
                Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
                foreach (var path in paths)
                {
                    try
                    {
                        weapons.Add(new WeaponOption(Path.GetFileName(path), SettingsService.LoadWeaponProfile(path)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        // Unreadable or invalid files are skipped so valid profiles can still be selected.
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                return Array.Empty<WeaponOption>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                loadError = ex.Message;
            }

            return weapons.AsReadOnly();
        }

        internal void SetOffset(int x, int y)
        {
            Offset = (x, y);
            _aimPositionHandler.SetOffset(x, y);
        }

    }
}
