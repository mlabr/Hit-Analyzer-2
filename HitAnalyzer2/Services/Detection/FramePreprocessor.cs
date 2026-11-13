using OpenCvSharp;
using System;

namespace HitAnalyzer2.Services.Detection
{
    internal static class FramePreprocessor
    {
        // The returned pixels are backed by either frame or output.
        internal static Mat ApplyImageAdjustments(Mat frame, Mat output, int brightnessPercent, int contrastPercent,
            int saturationPercent)
        {
            bool adjust = brightnessPercent != 50 || contrastPercent != 50 || saturationPercent != 50;
            using var converted = adjust && frame.Type() != MatType.CV_8UC4 ? new Mat() : null;
            var pixels = frame;
            if (frame.Type() != MatType.CV_8UC4)
            {
                pixels = converted ?? output;
                ConvertToBgra(frame, pixels);
            }

            if (!adjust)
                return pixels;

            ApplyBrightnessContrastAndSaturation(pixels, output, brightnessPercent, contrastPercent, saturationPercent);
            return output;
        }

        private static void ConvertToBgra(Mat frame, Mat output)
        {
            Cv2.CvtColor(frame, output, frame.Type() switch
            {
                var type when type == MatType.CV_8UC1 => ColorConversionCodes.GRAY2BGRA,
                var type when type == MatType.CV_8UC3 => ColorConversionCodes.BGR2BGRA,
                _ => throw new InvalidOperationException("Nepodporovaný formát snímku.")
            });
        }

        private static void ApplyBrightnessContrastAndSaturation(Mat pixels, Mat output, int brightnessPercent,
            int contrastPercent, int saturationPercent)
        {
            const double midGray = 127.5;
            double brightness = brightnessPercent / 50.0;
            double contrast = contrastPercent / 50.0;
            double scale = brightness * contrast;
            double offset = brightness * midGray * (1 - contrast);
            if (saturationPercent == 50)
                pixels.ConvertTo(output, MatType.CV_8UC4, scale, offset);
            else
                ApplyColorTransform(pixels, output, saturationPercent / 50.0, scale, offset);
        }

        private static void ApplyColorTransform(Mat pixels, Mat adjusted, double saturation, double scale, double offset)
        {
            // Mix each color channel with the pixel's grayscale value, applying brightness
            // and contrast in the same transform so the result is clamped only once.
            double grayScale = (1 - saturation) * scale;
            float blue = (float)(0.114 * grayScale);
            float green = (float)(0.587 * grayScale);
            float red = (float)(0.299 * grayScale);
            float color = (float)(saturation * scale);
            float bias = (float)offset;
            float[] coefficients =
            {
                blue + color, green,         red,         0, bias,
                blue,         green + color, red,         0, bias,
                blue,         green,         red + color, 0, bias,
                0,            0,             0,           1, 0
            };
            using var transform = Mat.FromPixelData(4, 5, MatType.CV_32FC1, coefficients);
            Cv2.Transform(pixels, adjusted, transform);
        }
    }
}
