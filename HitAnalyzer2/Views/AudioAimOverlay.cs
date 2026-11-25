using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HitAnalyzer2.Models;

namespace HitAnalyzer2.Views
{
    public sealed class AudioAimOverlay : Control
    {
        public static readonly StyledProperty<Point?> AimProperty =
            AvaloniaProperty.Register<AudioAimOverlay, Point?>(nameof(Aim));

        private static readonly Pen Outline = new(Brushes.White, 3);
        private static readonly Pen Marker = new(Brushes.Red, 1.5);

        static AudioAimOverlay() => AffectsRender<AudioAimOverlay>(AimProperty);

        public Point? Aim
        {
            get => GetValue(AimProperty);
            set => SetValue(AimProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            if (Aim is not { } aim)
                return;

            double size = Math.Min(Bounds.Width, Bounds.Height);
            double diameter = size / (1 + 2 * TargetPreviewModel.PreviewMargin);
            var point = new Point(Bounds.Width / 2 + (aim.X - TargetModel.Center.X) * diameter,
                Bounds.Height / 2 + (aim.Y - TargetModel.Center.Y) * diameter);
            var imageBounds = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
            if (size <= 0 || !imageBounds.Contains(point))
                return;

            using var clip = context.PushClip(imageBounds);
            DrawCross(Outline);
            DrawCross(Marker);

            void DrawCross(Pen pen)
            {
                const double radius = 6;
                context.DrawLine(pen, point - new Vector(radius, 0), point + new Vector(radius, 0));
                context.DrawLine(pen, point - new Vector(0, radius), point + new Vector(0, radius));
            }
        }
    }
}
