using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Shooting;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HitAnalyzer2.Services.Exporting
{
    internal static class ShootLogExportService
    {
        private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("cs-CZ");

        internal static string Export(ShootingSessionService.CompletedSession session)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "Exports");
            Directory.CreateDirectory(directory);
            string timestamp = session.StartedAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            double total = session.Hits.Sum(hit => hit.HitPosition.Score);
            string score = total.ToString("0.#########", CultureInfo.InvariantCulture);
            string hitCount = session.Hits.Count.ToString(CultureInfo.InvariantCulture);
            string shooter = FileNameShooter(session.Info.Shooter);
            string name = $"{timestamp}_{score}_{hitCount}_{shooter}";
            string pendingPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
            try
            {
                using (var writer = new StreamWriter(pendingPath, false, new UTF8Encoding(true)))
                    Write(writer, session, total);
                for (int copy = 1; ; copy++)
                {
                    string suffix = copy == 1 ? "" : "_" + copy.ToString(CultureInfo.InvariantCulture);
                    string path = Path.Combine(directory, name + suffix + ".slg");
                    try
                    {
                        File.Move(pendingPath, path);
                        return path;
                    }
                    catch (IOException) when (File.Exists(path))
                    {
                        // Another export already used this name.
                    }
                }
            }
            finally
            {
                if (File.Exists(pendingPath))
                    File.Delete(pendingPath);
            }
        }

        private static void Write(TextWriter writer, ShootingSessionService.CompletedSession session, double total)
        {
            writer.WriteLine("Shootlog version: 3");
            writer.WriteLine($"Date: {session.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
            writer.WriteLine($"Time: {session.StartedAt.ToString("HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)}");
            writer.WriteLine($"Shooter: {SingleLine(session.Info.Shooter)}");
            writer.WriteLine($"Weapon: {SingleLine(session.Info.Weapon)}");
            writer.WriteLine($"Caliber: {Number(session.CaliberMm)}");
            writer.WriteLine($"Target: {SingleLine(session.Target.TargetName)}");
            writer.WriteLine($"Target type: {session.Target.TargetType}");
            if (session.Target.MarkedRingPoints is { } markedRing)
                writer.WriteLine($"Marked ring: {Number(markedRing)}");
            writer.WriteLine($"Physical distance (m): {session.Info.RealRange.ToString(NumberCulture)}");
            writer.WriteLine($"Simulated distnce (m): {session.Info.SimulatedRange.ToString(NumberCulture)}");
            writer.WriteLine($"Elapsed (ms): {Milliseconds(session.Elapsed)}");
            writer.WriteLine($"Time limit (ms): {(session.TimeLimit is { } limit ? Milliseconds(limit) : "")}");
            writer.WriteLine($"Hit limit: {session.HitLimit?.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"End reason: {session.EndReason}");
            writer.WriteLine($"Hits: {session.Hits.Count.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"Total: {Number(total)}");

            writer.WriteLine();
            writer.WriteLine("[Hits]");
            writer.WriteLine("No;Time_ms;X_mm;Y_mm;Score");
            double? diameter = session.Target.MaxDiameterMm;
            for (int i = 0; i < session.Hits.Count; i++)
            {
                var hit = session.Hits[i].HitPosition;
                double? x = (hit.X - TargetModel.Center.X) * diameter;
                double? y = (TargetModel.Center.Y - hit.Y) * diameter;
                TimeSpan elapsed = hit.Time > session.HitTimeOrigin ? hit.Time - session.HitTimeOrigin : TimeSpan.Zero;
                writer.WriteLine($"{(i + 1).ToString(CultureInfo.InvariantCulture)};{Milliseconds(elapsed)};{Number(x)};{Number(y)};{Number(hit.Score)}");
            }

            writer.WriteLine();
            writer.WriteLine("[Target]");
            writer.WriteLine("Score;Diameter_mm");
            foreach (var ring in session.Target.Rings.OrderBy(ring => ring.PointValue))
                writer.WriteLine($"{Number(ring.PointValue)};{Number(ring.Diameter * session.Target.MillimetersPerUnit)}");
        }

        private static string SingleLine(string? value) => value?.Replace('\r', ' ').Replace('\n', ' ') ?? "";

        private static string FileNameShooter(string shooter)
        {
            const string invalid = "<>:\"/\\|?*";
            var result = new StringBuilder();
            foreach (var character in shooter.Trim().Normalize(NormalizationForm.FormD).EnumerateRunes())
            {
                var category = Rune.GetUnicodeCategory(character);
                if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                    continue;
                if (Rune.IsWhiteSpace(character) || category == UnicodeCategory.Control ||
                    (character.IsAscii && invalid.Contains((char)character.Value)))
                    result.Append('_');
                else
                    result.Append(character.ToString());
            }

            string name = result.ToString().Normalize(NormalizationForm.FormC).Trim().TrimEnd('.');
            return name.Length == 0 ? "Unknown" : name;
        }

        private static string Number(double? value) => value?.ToString("0.#", NumberCulture) ?? "";

        private static string Milliseconds(TimeSpan value) =>
            ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
    }
}
