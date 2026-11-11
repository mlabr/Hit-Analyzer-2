using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HitAnalyzer2.Services.Video
{
    internal sealed class CameraCaptureSession(
        Func<Mat, long, double?, CancellationToken, Task> processFrame,
        Action clearPreview, Action<string?> showStatus)
    {
        private static readonly TimeSpan CaptureStopTimeout = TimeSpan.FromSeconds(1);
        private const string CameraStillStopping = "Ovladač kamery stále ukončuje snímání. Zkuste to znovu později.";
        private readonly VideoHandler _videoHandler = new();
        private readonly SemaphoreSlim _cameraGate = new(1);
        private readonly CancellationTokenSource _discoveryCancellation = new();
        private readonly Dictionary<(string? DeviceId, int Index), Task> _pendingCaptures = new();
        private CancellationTokenSource? _captureCancellation;
        private Task? _captureTask;
        private (string? DeviceId, int Index) _captureDevice;
        private int _selectionVersion;
        private Task? _stopTask;

        internal bool IsClosing { get; private set; }

        internal async Task RefreshAsync(
            Func<IReadOnlyList<CameraDevice>, (CameraDevice? Camera, CameraMode? Mode, FlipMode? Flip)> selectCamera)
        {
            ++_selectionVersion;
            await _cameraGate.WaitAsync();
            try
            {
                if (IsClosing)
                    return;

                await StopCaptureAsync();
                if (IsClosing)
                    return;

                foreach (var pendingCapture in _pendingCaptures.Values)
                {
                    if (!pendingCapture.IsCompleted)
                    {
                        showStatus(CameraStillStopping);
                        return;
                    }
                }
                _pendingCaptures.Clear();

                showStatus("Hledám kamery…");
                var detected = await _videoHandler.DetectCamerasAsync(_discoveryCancellation.Token);
                if (IsClosing)
                    return;

                var (camera, mode, flip) = selectCamera(detected);
                if (camera is not null)
                    StartCapture(camera, mode, flip);
                else
                    showStatus("Nebyla nalezena žádná dostupná kamera.");
            }
            catch (OperationCanceledException) when (_discoveryCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                showStatus($"Kamery se nepodařilo vyhledat: {ex.Message}");
            }
            finally
            {
                _cameraGate.Release();
            }
        }

        internal async Task SwitchAsync(CameraDevice? camera, CameraMode? mode, FlipMode? flip)
        {
            int version = ++_selectionVersion;
            await _cameraGate.WaitAsync();
            try
            {
                if (IsClosing || version != _selectionVersion)
                    return;

                await StopCaptureAsync();
                if (IsClosing || version != _selectionVersion)
                    return;

                if (camera is not null)
                    StartCapture(camera, mode, flip);
                else
                    showStatus(null);
            }
            finally
            {
                _cameraGate.Release();
            }
        }

        internal Task StopAsync() => _stopTask ??= StopSessionAsync();

        private async Task StopSessionAsync()
        {
            IsClosing = true;
            _discoveryCancellation.Cancel();
            await _cameraGate.WaitAsync();
            try
            {
                await StopCaptureAsync();
            }
            finally
            {
                _discoveryCancellation.Dispose();
                _cameraGate.Release();
            }
        }

        private void StartCapture(CameraDevice camera, CameraMode? mode, FlipMode? flip)
        {
            var device = (camera.DeviceId, camera.DeviceId is null ? camera.Index : 0);
            if (_pendingCaptures.TryGetValue(device, out var pendingCapture) && !pendingCapture.IsCompleted)
            {
                showStatus(CameraStillStopping);
                return;
            }
            _pendingCaptures.Remove(device);

            showStatus("Otevírám kameru…");
            _captureDevice = device;
            _captureCancellation = new CancellationTokenSource();
            _captureTask = RunCaptureAsync(camera, mode, flip, _captureCancellation.Token);
        }

        private async Task StopCaptureAsync()
        {
            if (_captureCancellation is not { } cancellation)
                return;

            var capture = _captureTask!;
            cancellation.Cancel();
            try
            {
                await capture.WaitAsync(CaptureStopTimeout);
            }
            catch (TimeoutException)
            {
                // The capture worker owns the native handle until the driver call returns.
                _pendingCaptures[_captureDevice] = capture;
            }
            finally
            {
                _ = capture.ContinueWith(_ => cancellation.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                _captureCancellation = null;
                _captureTask = null;
                clearPreview();
            }
        }

        private async Task RunCaptureAsync(CameraDevice camera, CameraMode? mode, FlipMode? flip,
            CancellationToken cancellationToken)
        {
            try
            {
                await _videoHandler.CaptureAsync(camera, mode,
                    (frame, capturedAt, fps) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return processFrame(frame, capturedAt, fps, cancellationToken);
                    }, cancellationToken, flip);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                clearPreview();
                showStatus($"Chyba kamery: {ex.Message}");
            }
        }
    }
}
