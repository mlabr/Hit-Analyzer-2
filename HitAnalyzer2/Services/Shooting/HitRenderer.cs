using OpenCvSharp;
using System;

namespace HitAnalyzer2.Services.Shooting
{
    internal static class HitRenderer
    {
        private const double MarkerSizeRatio = 0.05;
        private static readonly Scalar MarkerColor = new(0, 255, 0, 255);

        internal static void Draw(Mat preview, LaserPosition position)
        {
            int x = (int)Math.Round(position.X);
            int y = (int)Math.Round(position.Y);
            int size = Math.Max(1, (int)Math.Round(preview.Height * MarkerSizeRatio));
            int startOffset = (size - 1) / 2;
            int endOffset = size / 2;

            Cv2.Line(preview, new Point(x - startOffset, y), new Point(x + endOffset, y), MarkerColor, 2);
            Cv2.Line(preview, new Point(x, y - startOffset), new Point(x, y + endOffset), MarkerColor, 2);
        }
    }
}
