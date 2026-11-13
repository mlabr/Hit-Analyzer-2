using HitAnalyzer2.Services.Shooting;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitAnalyzer2.Repository
{
    internal sealed class WeaponFileRepository(string directory)
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter<TriggerTypeEnum>() }
        };

        public IReadOnlyList<SavedWeaponProfile> LoadProfileList(out string? loadError)
        {
            loadError = null;
            string[] paths;
            try
            {
                paths = Directory.GetFiles(directory, "*.json");
            }
            catch (DirectoryNotFoundException)
            {
                return Array.Empty<SavedWeaponProfile>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                loadError = ex.Message;
                return Array.Empty<SavedWeaponProfile>();
            }

            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            var profiles = new List<SavedWeaponProfile>(paths.Length);
            foreach (var path in paths)
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var profile = JsonSerializer.Deserialize<WeaponProfile>(stream, JsonOptions);
                    ValidateProfile(profile);
                    profiles.Add(new SavedWeaponProfile(Path.GetFileName(path), profile));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // A single unreadable or invalid profile must not hide the others.
                }
            }

            return profiles.AsReadOnly();
        }

        public void SaveProfile(SavedWeaponProfile profile, Action? commitRelatedChanges = null)
        {
            var path = GetProfilePath(profile.FileName);
            ValidateProfile(profile.Profile);

            var temporaryPath = JsonFile.WriteTemporaryFile(path, profile.Profile, JsonOptions);
            var backupPath = temporaryPath + ".bak";
            try
            {
                bool hadProfile = File.Exists(path);
                if (hadProfile)
                    File.Replace(temporaryPath, path, backupPath);
                else
                    File.Move(temporaryPath, path);

                try
                {
                    commitRelatedChanges?.Invoke();
                }
                catch
                {
                    if (hadProfile)
                        File.Move(backupPath, path, overwrite: true);
                    else
                        File.Delete(path);

                    throw;
                }

                File.Delete(backupPath);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        public void RestoreMissingProfile(SavedWeaponProfile profile)
        {
            var path = GetProfilePath(profile.FileName);
            ValidateProfile(profile.Profile);
            if (File.Exists(path))
                return;

            try
            {
                JsonFile.Write(path, profile.Profile, overwrite: false, JsonOptions);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another instance restored the profile first.
            }
        }

        private string GetProfilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.Contains('/') || fileName.Contains('\\')
                || !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
                throw new JsonException("Neplatný název souboru profilu zbraně.");

            return Path.Combine(directory, fileName);
        }

        internal static void ValidateProfile([NotNull] WeaponProfile? profile)
        {
            if (profile is not { Type: "ha_weapon" } || string.IsNullOrWhiteSpace(profile.Name))
                throw new JsonException("Neplatný typ nebo název profilu zbraně.");

            ValidateSettings(profile);
        }

        internal static void ValidateSettings(WeaponSettings weapon)
        {
            if (!Enum.IsDefined(weapon.Trigger)
                || weapon.NexDetectionInterval is < ShootingSessionService.NextDetectionMinimumInterval
                    or > ShootingSessionService.NextDetectionMaximumInterval
                || weapon.OffsetX is < WeaponHandler.MinimumOffset or > WeaponHandler.MaximumOffset
                || weapon.OffsetY is < WeaponHandler.MinimumOffset or > WeaponHandler.MaximumOffset)
                throw new JsonException("Neplatné nastavení zbraně.");
        }
    }
}
