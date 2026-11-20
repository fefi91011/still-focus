using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Still.App.Controls;

public sealed class ProgressRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Value), 1);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Fill), Brush.Parse("#C4F5A9"));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Track), Brush.Parse("#222933"));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(StrokeThickness), 10);

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(ValueProperty, FillProperty, TrackProperty, StrokeThicknessProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var thickness = Math.Clamp(StrokeThickness, 2, 40);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - thickness / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, new Pen(Track, thickness), center, radius, radius);

        var value = Math.Clamp(Value, 0, 1);
        if (value <= 0.001)
        {
            return;
        }

        var pen = new Pen(Fill, thickness)
        {
            LineCap = PenLineCap.Round
        };

        if (value >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var startAngle = -Math.PI / 2;
        var endAngle = startAngle + value * Math.PI * 2;
        var geometry = new StreamGeometry();

        using (var stream = geometry.Open())
        {
            stream.BeginFigure(PointOnCircle(center, radius, startAngle), false);
            stream.ArcTo(
                PointOnCircle(center, radius, endAngle),
                new Size(radius, radius),
                0,
                value > 0.5,
                SweepDirection.Clockwise);
            stream.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle) =>
        new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
}
