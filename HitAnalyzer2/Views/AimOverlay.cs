using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HitAnalyzer2.Views
{
    public sealed class AimOverlay : Control
    {
        public static readonly StyledProperty<Point?> AimProperty =
            AvaloniaProperty.Register<AimOverlay, Point?>(nameof(Aim));

        public static readonly StyledProperty<Size> SourceSizeProperty =
            AvaloniaProperty.Register<AimOverlay, Size>(nameof(SourceSize));

        public static readonly StyledProperty<StretchDirection> StretchDirectionProperty =
            AvaloniaProperty.Register<AimOverlay, StretchDirection>(nameof(StretchDirection), StretchDirection.Both);

        private static readonly Pen Outline = new(Brushes.White, 3);
        private static readonly Pen Marker = new(Brushes.Red, 1.5);

        static AimOverlay() => AffectsRender<AimOverlay>(AimProperty, SourceSizeProperty, StretchDirectionProperty, BoundsProperty);

        public Point? Aim
        {
            get => GetValue(AimProperty);
            set => SetValue(AimProperty, value);
        }

        public Size SourceSize
        {
            get => GetValue(SourceSizeProperty);
            set => SetValue(SourceSizeProperty, value);
        }

        public StretchDirection StretchDirection
        {
            get => GetValue(StretchDirectionProperty);
            set => SetValue(StretchDirectionProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            if (Aim is not { X: >= 0 and <= 1, Y: >= 0 and <= 1 } aim ||
                SourceSize.Width <= 0 || SourceSize.Height <= 0)
                return;

            var size = Stretch.Uniform.CalculateSize(Bounds.Size, SourceSize, StretchDirection);
            if (size.Width <= 0 || size.Height <= 0)
                return;

            var imageBounds = new Rect((Bounds.Width - size.Width) / 2,
                (Bounds.Height - size.Height) / 2, size.Width, size.Height);
            var point = new Point(imageBounds.X + aim.X * size.Width, imageBounds.Y + aim.Y * size.Height);
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
