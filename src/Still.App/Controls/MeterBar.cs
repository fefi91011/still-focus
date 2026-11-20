using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Still.App.Controls;

public sealed class MeterBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<MeterBar, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<MeterBar, IBrush?>(nameof(Fill), Brush.Parse("#C4F5A9"));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<MeterBar, IBrush?>(nameof(Track), Brush.Parse("#242B35"));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<MeterBar, double>(nameof(Thickness), 6);

    static MeterBar()
    {
        AffectsRender<MeterBar>(ValueProperty, FillProperty, TrackProperty, ThicknessProperty);
        HeightProperty.OverrideDefaultValue<MeterBar>(6);
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

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var thickness = Math.Clamp(Thickness, 2, Math.Max(2, Bounds.Height));
        var top = (Bounds.Height - thickness) / 2;
        var trackRect = new Rect(0, top, Bounds.Width, thickness);
        var radius = thickness / 2;

        context.DrawRectangle(Track, null, trackRect, radius, radius);

        var value = Math.Clamp(Value, 0, 1);
        if (value <= 0 || Bounds.Width <= 0)
        {
            return;
        }

        var width = Bounds.Width * value;
        context.DrawRectangle(Fill, null, new Rect(0, top, width, thickness), radius, radius);
    }
}
