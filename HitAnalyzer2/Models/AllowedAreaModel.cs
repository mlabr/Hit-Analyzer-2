using OpenCvSharp;
using System;

namespace HitAnalyzer2
{
    internal static class AllowedAreaModel
    {
        internal static Rect GetBorders(Mat frame, DetectionSettings settings)
        {
            int horizontalMargin = Math.Clamp(settings.HorizontalForbiddenZoneSize, 0, frame.Width);
            int verticalMargin = Math.Clamp(settings.VerticalForbiddenZoneSize, 0, frame.Height);
            return new Rect(horizontalMargin, verticalMargin,
                Math.Max(0, frame.Width - horizontalMargin - horizontalMargin),
                Math.Max(0, frame.Height - verticalMargin - verticalMargin));
        }
    }
}
