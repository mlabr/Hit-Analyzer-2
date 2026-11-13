using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HitAnalyzer2.Services.Video
{
    internal sealed unsafe class WindowsCamera : IDisposable
    {
        private const uint FirstVideoStream = 0xfffffffc;
        private const uint CurrentNativeType = uint.MaxValue;
        private const int NoMoreTypes = unchecked((int)0xc00d36b9);
        private const uint StreamEndedOrFailed = 0x03;
        private const uint NativeOrOutputTypeChanged = 0x30;

        private static readonly Guid SourceType = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
        private static readonly Guid VideoCaptureSource = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
        internal static readonly Guid SymbolicLink = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
        private static readonly Guid AdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
        private static readonly Guid SourceReaderEx = new("7b981cf0-560e-4116-9875-b099895f23d7");
        private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid VideoType = new("73646976-0000-0010-8000-00aa00389b71");
        private static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");
        private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        private static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");

        private readonly Session _session = new();
        private readonly CameraMode? _mode;
        private nint _source;
        private nint _reader;
        private int _width;
        private int _height;
        private int _stride;

        public WindowsCamera(string deviceId, CameraMode? mode)
        {
            _mode = mode;
            try
            {
                _source = OpenSource(deviceId);
                _reader = CreateReader(_source, convertVideo: true);
                if (mode is not null)
                    SelectNativeMode(mode);
                SetRgbOutput();
                UpdateOutputLayout();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void SelectNativeMode(CameraMode mode)
        {
            Check(Marshal.QueryInterface(_reader, in SourceReaderEx, out var readerEx));
            try
            {
                for (uint index = 0; TryGetNativeType(_reader, index, out var type); index++)
                {
                    try
                    {
                        if (ReadMode(type) != mode)
                            continue;

                        // IMFSourceReaderEx::SetNativeMediaType
                        var setNativeType = (delegate* unmanaged[Stdcall]<nint, uint, nint, uint*, int>)Slot(readerEx, 13);
                        uint flags;
                        Check(setNativeType(readerEx, FirstVideoStream, type, &flags));
                        return;
                    }
                    finally
                    {
                        Release(type);
                    }
                }
            }
            finally
            {
                Release(readerEx);
            }

            throw new InvalidOperationException("Vybraný režim už kamera nenabízí. Obnovte seznam kamer.");
        }

        private void SetRgbOutput()
        {
            Check(MFCreateMediaType(out var type));
            try
            {
                SetGuid(type, MajorType, VideoType);
                SetGuid(type, Subtype, Rgb32);
                // IMFSourceReader::SetCurrentMediaType
                var setCurrentType = (delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Slot(_reader, 7);
                Check(setCurrentType(_reader, FirstVideoStream, 0, type));
            }
            finally
            {
                Release(type);
            }
        }

        private void UpdateOutputLayout()
        {
            if (_mode is { } mode)
            {
                if (!TryGetNativeType(_reader, CurrentNativeType, out var nativeType))
                    throw new InvalidOperationException("Kamera nehlásí aktuální režim.");
                try
                {
                    if (ReadMode(nativeType) != mode)
                        throw new InvalidOperationException("Kamera nepoužila vybraný režim.");
                }
                finally
                {
                    Release(nativeType);
                }
            }

            // IMFSourceReader::GetCurrentMediaType
            var getCurrentType = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(_reader, 6);
            nint type;
            Check(getCurrentType(_reader, FirstVideoStream, &type));
            try
            {
                if (!TryGetUInt64(type, FrameSize, out var size) || GetGuid(type, Subtype) != Rgb32)
                    throw new InvalidOperationException("Kamera neposkytuje očekávaný formát náhledu.");

                _width = checked((int)(size >> 32));
                _height = checked((int)(size & uint.MaxValue));
                if (_mode is { } selected && (_width != selected.Width || _height != selected.Height))
                    throw new InvalidOperationException("Náhled nemá vybrané rozlišení.");

                var strideKey = DefaultStride;
                var getUInt32 = (delegate* unmanaged[Stdcall]<nint, Guid*, int*, int>)Slot(type, 7);
                int stride;
                if (getUInt32(type, &strideKey, &stride) < 0)
                    Check(MFGetStrideForBitmapInfoHeader(22, (uint)_width, out stride));
                _stride = stride;
            }
            finally
            {
                Release(type);
            }
        }

        public bool Read(Mat frame, CancellationToken cancellationToken)
        {
            // IMFSourceReader::ReadSample
            var readSample = (delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)Slot(_reader, 9);
            while (!cancellationToken.IsCancellationRequested)
            {
                uint flags;
                nint sample;
                Check(readSample(_reader, FirstVideoStream, 0, null, &flags, null, &sample));
                try
                {
                    if ((flags & StreamEndedOrFailed) != 0 || cancellationToken.IsCancellationRequested)
                        return false;
                    if ((flags & NativeOrOutputTypeChanged) != 0)
                        UpdateOutputLayout();
                    if (sample == 0)
                        continue;

                    CopySample(sample, frame);
                    return true;
                }
                finally
                {
                    Release(sample);
                }
            }
            return false;
        }

        private void CopySample(nint sample, Mat frame)
        {
            // IMFSample::ConvertToContiguousBuffer
            var convert = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(sample, 41);
            nint buffer;
            Check(convert(sample, &buffer));
            try
            {
                // IMFMediaBuffer::Lock / Unlock
                var lockBuffer = (delegate* unmanaged[Stdcall]<nint, nint*, uint*, uint*, int>)Slot(buffer, 3);
                var unlockBuffer = (delegate* unmanaged[Stdcall]<nint, int>)Slot(buffer, 4);
                nint pixels;
                uint length;
                Check(lockBuffer(buffer, &pixels, null, &length));
                try
                {
                    int stride = Math.Abs(_stride);
                    int rowBytes = checked(_width * 4);
                    if (_height <= 0 || stride < rowBytes || length < (long)(_height - 1) * stride + rowBytes)
                        throw new InvalidOperationException("Kamera vrátila neúplný snímek.");

                    using var source = Mat.FromPixelData(_height, _width, MatType.CV_8UC4, pixels, stride);
                    if (_stride < 0)
                        Cv2.Flip(source, frame, FlipMode.X);
                    else
                        source.CopyTo(frame);
                }
                finally
                {
                    unlockBuffer(buffer);
                }
            }
            finally
            {
                Release(buffer);
            }
        }

        internal static CameraMode? ReadMode(nint type)
        {
            if (!TryGetUInt64(type, FrameSize, out var size) ||
                !TryGetUInt64(type, FrameRate, out var rate) || (uint)rate == 0 ||
                GetGuid(type, Subtype) is not { } format)
                return null;

            uint numerator = (uint)(rate >> 32);
            uint denominator = (uint)rate;
            uint a = numerator;
            uint b = denominator;
            while (b != 0)
                (a, b) = (b, a % b);

            return new CameraMode(checked((int)(size >> 32)), checked((int)(size & uint.MaxValue)),
                numerator / a, denominator / a, format);
        }

        internal static bool TryGetNativeType(nint reader, uint index, out nint type)
        {
            // IMFSourceReader::GetNativeMediaType
            var getNativeType = (delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, int>)Slot(reader, 5);
            nint result;
            int hr = getNativeType(reader, FirstVideoStream, index, &result);
            if (hr != NoMoreTypes)
                Check(hr);
            type = hr == NoMoreTypes ? 0 : result;
            return hr != NoMoreTypes;
        }

        internal static nint CreateDeviceAttributes()
        {
            Check(MFCreateAttributes(out var attributes, 2));
            try
            {
                SetGuid(attributes, SourceType, VideoCaptureSource);
                return attributes;
            }
            catch
            {
                Release(attributes);
                throw;
            }
        }

        internal static nint OpenSource(string deviceId)
        {
            nint attributes = CreateDeviceAttributes();
            try
            {
                var key = SymbolicLink;
                // IMFAttributes::SetString
                var setString = (delegate* unmanaged[Stdcall]<nint, Guid*, char*, int>)Slot(attributes, 25);
                fixed (char* value = deviceId)
                    Check(setString(attributes, &key, value));
                Check(MFCreateDeviceSource(attributes, out var source));
                return source;
            }
            finally
            {
                Release(attributes);
            }
        }

        internal static nint CreateReader(nint source, bool convertVideo)
        {
            nint attributes = 0;
            try
            {
                if (convertVideo)
                {
                    Check(MFCreateAttributes(out attributes, 1));
                    var key = AdvancedVideoProcessing;
                    // IMFAttributes::SetUINT32
                    var setUInt32 = (delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)Slot(attributes, 21);
                    Check(setUInt32(attributes, &key, 1));
                }
                Check(MFCreateSourceReaderFromMediaSource(source, attributes, out var reader));
                return reader;
            }
            finally
            {
                Release(attributes);
            }
        }

        private static Guid? GetGuid(nint attributes, Guid key)
        {
            var getGuid = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(attributes, 10);
            Guid value;
            return getGuid(attributes, &key, &value) >= 0 ? value : null;
        }

        private static void SetGuid(nint attributes, Guid key, Guid value)
        {
            var setGuid = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(attributes, 24);
            Check(setGuid(attributes, &key, &value));
        }

        private static bool TryGetUInt64(nint attributes, Guid key, out ulong value)
        {
            var getUInt64 = (delegate* unmanaged[Stdcall]<nint, Guid*, ulong*, int>)Slot(attributes, 8);
            ulong result;
            bool succeeded = getUInt64(attributes, &key, &result) >= 0;
            value = succeeded ? result : 0;
            return succeeded;
        }

        internal static nint Slot(nint instance, int index) => (*(nint**)instance)[index];
        internal static void Check(int result)
        {
            if (result < 0)
            {
                var error = Marshal.GetExceptionForHR(result, new nint(-1));
                throw new COMException(error?.Message, result);
            }
        }

        internal static void Release(nint instance)
        {
            if (instance != 0)
                Marshal.Release(instance);
        }

        internal static void CloseSource(nint source, nint reader)
        {
            // By default releasing the reader shuts down the media source.
            Release(reader);
            if (source == 0)
                return;
            if (reader == 0)
            {
                var shutdown = (delegate* unmanaged[Stdcall]<nint, int>)Slot(source, 12);
                shutdown(source);
            }
            Release(source);
        }

        public void Dispose()
        {
            CloseSource(_source, _reader);
            _source = 0;
            _reader = 0;
            _session.Dispose();
        }

        internal sealed class Session : IDisposable
        {
            private bool _started;

            public Session()
            {
                Check(CoInitializeEx(0, 0)); // COINIT_MULTITHREADED
                try
                {
                    Check(MFStartup(0x00020070, 0)); // MF_VERSION, MFSTARTUP_FULL
                    _started = true;
                }
                catch
                {
                    CoUninitialize();
                    throw;
                }
            }

            public void Dispose()
            {
                if (!_started)
                    return;
                _started = false;
                MFShutdown();
                CoUninitialize();
            }
        }

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoInitializeEx(nint reserved, uint concurrency);

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern void CoUninitialize();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(uint version, uint flags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateAttributes(out nint attributes, uint initialSize);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMediaType(out nint mediaType);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFGetStrideForBitmapInfoHeader(uint format, uint width, out int stride);

        [DllImport("mf.dll", ExactSpelling = true)]
        private static extern int MFCreateDeviceSource(nint attributes, out nint source);

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        private static extern int MFCreateSourceReaderFromMediaSource(nint source, nint attributes, out nint reader);
    }
}
