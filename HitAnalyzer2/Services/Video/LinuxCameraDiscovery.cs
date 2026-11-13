using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace HitAnalyzer2.Services.Video
{
    internal static class LinuxCameraDiscovery
    {
        private static readonly Regex FormatLine = new(@"^\s*\[\d+\]:\s*'(.{4})'");
        private static readonly Regex SizeLine = new(@"^\s*Size:\s*Discrete\s+(\d+)x(\d+)");
        private static readonly Regex IntervalLine = new(@"^\s*Interval:\s*Discrete\s+.*\(([0-9.]+) fps\)");

        public static IReadOnlyList<CameraDevice> Detect(CancellationToken cancellationToken)
        {
            var cameras = new List<CameraDevice>();
            foreach (var camera in IndexedCameraDiscovery.Detect(VideoCaptureAPIs.V4L2, cancellationToken))
            {
                try
                {
                    cameras.Add(camera with { Modes = DetectModes(camera.Index, cancellationToken) });
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
                {
                    cameras.Add(camera with { ModeDetectionError = ex.Message });
                }
            }

            return cameras;
        }

        private static CameraMode[] DetectModes(int index, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo("v4l2-ctl")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--device");
            startInfo.ArgumentList.Add($"/dev/video{index}");
            startInfo.ArgumentList.Add("--list-formats-ext");
            startInfo.Environment["LC_ALL"] = "C";

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Nepodařilo se spustit v4l2-ctl.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).GetAwaiter().GetResult();
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill();
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException(error.GetAwaiter().GetResult().Trim());

            return Parse(output.GetAwaiter().GetResult());
        }

        private static CameraMode[] Parse(string output)
        {
            var modes = new HashSet<CameraMode>();
            Guid format = Guid.Empty;
            int width = 0;
            int height = 0;
            foreach (string line in output.Split('\n'))
            {
                var formatMatch = FormatLine.Match(line);
                if (formatMatch.Success)
                {
                    string code = formatMatch.Groups[1].Value;
                    int fourCc = VideoFourCc(code);
                    format = new Guid(fourCc, 0, 0x0010, 0x80, 0, 0, 0xaa, 0, 0x38, 0x9b, 0x71);
                    width = height = 0;
                    continue;
                }

                if (line.TrimStart().StartsWith("Size:", StringComparison.Ordinal))
                {
                    var size = SizeLine.Match(line);
                    int.TryParse(size.Groups[1].Value, out width);
                    int.TryParse(size.Groups[2].Value, out height);
                    continue;
                }

                if (format == Guid.Empty || width < 640 || height < 480)
                    continue;

                var interval = IntervalLine.Match(line);
                if (interval.Success &&
                    decimal.TryParse(interval.Groups[1].Value, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out decimal fps) &&
                    fps >= 25 && fps <= uint.MaxValue / 1000m)
                {
                    modes.Add(new CameraMode(width, height, (uint)(fps * 1000), 1000, format));
                }
            }

            return modes.OrderBy(mode => (long)mode.Width * mode.Height)
                .ThenByDescending(mode => mode.FramesPerSecond)
                .ThenBy(mode => mode.Format)
                .ToArray();
        }

        private static int VideoFourCc(string code) =>
            code[0] | (code[1] << 8) | (code[2] << 16) | (code[3] << 24);
    }
}
