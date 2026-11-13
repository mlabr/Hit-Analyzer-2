using OpenCvSharp;
using System.Collections.Generic;
using System.Threading;

namespace HitAnalyzer2.Services.Video
{
    internal static class IndexedCameraDiscovery
    {
        private const int CameraProbeLimit = 10;

        internal static IEnumerable<CameraDevice> Detect(VideoCaptureAPIs api, CancellationToken cancellationToken)
        {
            for (int index = 0; index < CameraProbeLimit; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var capture = new VideoCapture(index, api))
                {
                    if (!capture.IsOpened())
                        continue;
                }

                yield return new CameraDevice(index);
            }
        }
    }
}
