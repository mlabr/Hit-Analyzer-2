using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using static HitAnalyzer2.Services.Video.WindowsCamera;

namespace HitAnalyzer2.Services.Video
{
    internal static unsafe class WindowsCameraDiscovery
    {
        private static readonly Guid FriendlyName = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");

        public static IReadOnlyList<CameraDevice> Detect(CancellationToken cancellationToken)
        {
            using var session = new Session();
            nint attributes = CreateDeviceAttributes();
            nint* devices = null;
            uint count = 0;
            try
            {
                Check(MFEnumDeviceSources(attributes, out devices, out count));
                var cameras = new List<CameraDevice>();
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = GetString(devices[i], SymbolicLink);
                    if (string.IsNullOrEmpty(id))
                        continue;

                    var camera = new CameraDevice(i, GetString(devices[i], FriendlyName), id);
                    try
                    {
                        camera = camera with { Modes = DetectModes(id, cancellationToken) };
                    }
                    catch (COMException ex)
                    {
                        camera = camera with { ModeDetectionError = ex.Message };
                    }
                    cameras.Add(camera);
                }
                return cameras;
            }
            finally
            {
                for (int i = 0; i < count; i++)
                    Release(devices[i]);
                Marshal.FreeCoTaskMem((nint)devices);
                Release(attributes);
            }
        }

        private static CameraMode[] DetectModes(string deviceId, CancellationToken cancellationToken)
        {
            nint source = OpenSource(deviceId);
            nint reader = 0;
            try
            {
                reader = CreateReader(source, convertVideo: false);
                var modes = new HashSet<CameraMode>();
                for (uint index = 0; ; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryGetNativeType(reader, index, out var type))
                        break;

                    try
                    {
                        if (ReadMode(type) is { Width: >= 640, Height: >= 480 } mode &&
                            mode.FramesPerSecond >= 25)
                            modes.Add(mode);
                    }
                    finally
                    {
                        Release(type);
                    }
                }
                return modes.OrderBy(mode => (long)mode.Width * mode.Height)
                    .ThenByDescending(mode => mode.FramesPerSecond)
                    .ThenBy(mode => mode.Format)
                    .ToArray();
            }
            finally
            {
                CloseSource(source, reader);
            }
        }

        private static string? GetString(nint attributes, Guid key)
        {
            // IMFAttributes::GetAllocatedString
            var getString = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, uint*, int>)Slot(attributes, 13);
            nint value;
            uint length;
            if (getString(attributes, &key, &value, &length) < 0)
                return null;
            try
            {
                return Marshal.PtrToStringUni(value, checked((int)length));
            }
            finally
            {
                Marshal.FreeCoTaskMem(value);
            }
        }

        [DllImport("mf.dll", ExactSpelling = true)]
        private static extern int MFEnumDeviceSources(nint attributes, out nint* devices, out uint count);
    }
}
