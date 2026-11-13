using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HitAnalyzer2.Services.Video
{
    public sealed record CameraMode(int Width, int Height, uint FpsNumerator, uint FpsDenominator, Guid Format)
    {
        [JsonIgnore]
        public double FramesPerSecond => (double)FpsNumerator / FpsDenominator;

        public override string ToString() => $"{Width} × {Height} · {FramesPerSecond:0.##} FPS · {FormatName}";

        private string FormatName
        {
            get
            {
                byte[] bytes = Format.ToByteArray();
                int code = BinaryPrimitives.ReadInt32LittleEndian(bytes);
                return code switch
                {
                    22 => "RGB32",
                    21 => "ARGB32",
                    20 => "RGB24",
                    _ when bytes[0] is >= 32 and <= 126 && bytes[1] is >= 32 and <= 126 &&
                           bytes[2] is >= 32 and <= 126 && bytes[3] is >= 32 and <= 126 =>
                        System.Text.Encoding.ASCII.GetString(bytes, 0, 4),
                    _ => Format.ToString()
                };
            }
        }
    }

    public sealed record CameraDevice(int Index, string? Name = null, string? DeviceId = null)
    {
        public IReadOnlyList<CameraMode> Modes { get; init; } = Array.Empty<CameraMode>();
        public string? ModeDetectionError { get; init; }

        public override string ToString() => Name ?? $"Kamera {Index}";
    }

    internal sealed class VideoHandler
    {
        public Task<IReadOnlyList<CameraDevice>> DetectCamerasAsync(CancellationToken cancellationToken)
        {
            return Task.Run<IReadOnlyList<CameraDevice>>(() =>
            {
                if (OperatingSystem.IsWindows())
                    return WindowsCameraDiscovery.Detect(cancellationToken);
                if (OperatingSystem.IsLinux())
                    return LinuxCameraDiscovery.Detect(cancellationToken);

                return IndexedCameraDiscovery.Detect(VideoCaptureAPIs.ANY, cancellationToken).ToArray();
            }, cancellationToken);
        }

        public Task CaptureAsync(
            CameraDevice camera,
            CameraMode? mode,
            Func<Mat, long, double?, Task> onFrame,
            CancellationToken cancellationToken,
            FlipMode? flip = null)
        {
            return Task.Factory.StartNew(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (camera.DeviceId is { } deviceId)
                {
                    using var windowsCapture = new WindowsCamera(deviceId, mode);
                    CaptureFrames(frame => windowsCapture.Read(frame, cancellationToken));
                    return;
                }

                using var capture = OpenCapture(camera.Index);
                if (!capture.IsOpened())
                    throw new InvalidOperationException($"Nepodařilo se otevřít kameru s indexem {camera.Index}.");

                if (OperatingSystem.IsLinux() && mode is not null)
                    ApplyLinuxMode(capture, mode);

                CaptureFrames(capture.Read);
            }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            void CaptureFrames(Func<Mat, bool> readFrame)
            {
                using var frame = new Mat();
                long? previousFrameTime = null;

                while (!cancellationToken.IsCancellationRequested)
                {
                    bool hasFrame = readFrame(frame);
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (!hasFrame || frame.Empty())
                        throw new InvalidOperationException("Z kamery se nepodařilo načíst snímek.");

                    long frameTime = Stopwatch.GetTimestamp();
                    double? fps = previousFrameTime is { } previous && frameTime > previous
                        ? 1 / Stopwatch.GetElapsedTime(previous, frameTime).TotalSeconds
                        : null;
                    previousFrameTime = frameTime;

                    if (flip is { } direction)
                        Cv2.Flip(frame, frame, direction);

                    // The frame remains valid until the consumer's task completes.
                    onFrame(frame, frameTime, fps).GetAwaiter().GetResult();
                }
            }
        }

        private static VideoCapture OpenCapture(int index) => OperatingSystem.IsLinux()
            ? new VideoCapture(index, VideoCaptureAPIs.V4L2)
            : new VideoCapture(index);

        private static void ApplyLinuxMode(VideoCapture capture, CameraMode mode)
        {
            int fourCc = BinaryPrimitives.ReadInt32LittleEndian(mode.Format.ToByteArray());
            // V4L2 in OpenCV accepts a whole-number FPS request.
            int fps = (int)Math.Round(mode.FramesPerSecond);
            if (!capture.Set(VideoCaptureProperties.FourCC, fourCc) ||
                !capture.Set(VideoCaptureProperties.FrameWidth, mode.Width) ||
                !capture.Set(VideoCaptureProperties.FrameHeight, mode.Height) ||
                !capture.Set(VideoCaptureProperties.Fps, fps) ||
                capture.Get(VideoCaptureProperties.FrameWidth) != mode.Width ||
                capture.Get(VideoCaptureProperties.FrameHeight) != mode.Height ||
                capture.Get(VideoCaptureProperties.FourCC) != fourCc ||
                Math.Round(capture.Get(VideoCaptureProperties.Fps)) != fps)
            {
                throw new InvalidOperationException($"Kamera nepřijala vybraný režim {mode}.");
            }
        }
    }
}
