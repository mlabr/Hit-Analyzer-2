using OpenCvSharp;
using System;

namespace HitAnalyzer2.Services.Detection
{
    internal static class AllowedArea
    {
        internal static Rect GetBounds(Mat frame, DetectionSettings settings)
        {
            int horizontalMargin = Math.Clamp(settings.HorizontalForbiddenZoneSize, 0, frame.Width);
            int verticalMargin = Math.Clamp(settings.VerticalForbiddenZoneSize, 0, frame.Height);
            return new Rect(horizontalMargin, verticalMargin,
                Math.Max(0, frame.Width - horizontalMargin - horizontalMargin),
                Math.Max(0, frame.Height - verticalMargin - verticalMargin));
        }
    }
}
