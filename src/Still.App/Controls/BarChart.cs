using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Still.App.Controls;

public sealed class ChartBar
{
    public string Label { get; init; } = string.Empty;
    public double Value { get; init; }
    public string ValueLabel { get; init; } = string.Empty;
    public bool Highlight { get; init; }
}

public sealed class BarChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartBar>?> BarsProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<ChartBar>?>(nameof(Bars));

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(BarBrush), Brush.Parse("#3A4654"));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(TrackBrush), Brush.Parse("#1A2029"));

    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(HighlightBrush), Brush.Parse("#C4F5A9"));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(LabelBrush), Brush.Parse("#8C96A6"));

    public static readonly StyledProperty<IBrush?> ValueBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(ValueBrush), Brush.Parse("#E8ECF2"));

    static BarChart()
    {
        AffectsRender<BarChart>(
            BarsProperty,
            BarBrushProperty,
            TrackBrushProperty,
            HighlightBrushProperty,
            LabelBrushProperty,
            ValueBrushProperty);
    }

    public IReadOnlyList<ChartBar>? Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush? ValueBrush
    {
        get => GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bars = Bars;
        if (bars is null || bars.Count == 0)
        {
            DrawCentered(context, "Noch keine Daten", Bounds, 12, LabelBrush ?? Brushes.Gray);
            return;
        }

        const double labelHeight = 24;
        const double topPadding = 22;
        var chartHeight = Math.Max(10, Bounds.Height - labelHeight - topPadding);
        var slot = Bounds.Width / bars.Count;
        var maxValue = Math.Max(1, bars.Max(b => b.Value));

        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var width = Math.Min(38, slot * 0.5);
            var x = i * slot + (slot - width) / 2;
            var height = bar.Value <= 0 ? 0 : Math.Max(4, bar.Value / maxValue * chartHeight * 0.82);
            var trackRect = new Rect(x, topPadding, width, chartHeight);
            context.DrawRectangle(TrackBrush, null, trackRect, 7, 7);

            if (height > 0)
            {
                context.DrawRectangle(
                    bar.Highlight ? HighlightBrush : BarBrush,
                    null,
                    new Rect(x, topPadding + chartHeight - height, width, height),
                    7,
                    7);
            }

            if (!string.IsNullOrEmpty(bar.ValueLabel))
            {
                var valueText = Text(bar.ValueLabel, 10, ValueBrush ?? Brushes.White, FontWeight.SemiBold);
                context.DrawText(
                    valueText,
                    new Point(x + (width - valueText.Width) / 2, topPadding + chartHeight - height - 17));

                var label = Text(bar.Label, 10, (bar.Highlight ? HighlightBrush : LabelBrush) ?? Brushes.Gray, FontWeight.Normal);
                context.DrawText(label, new Point(x + (width - label.Width) / 2, topPadding + chartHeight + 7));
            }
        }
    }

    private static FormattedText Text(string text, double size, IBrush brush, FontWeight weight) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    private static void DrawCentered(DrawingContext context, string text, Rect bounds, double size, IBrush brush)
    {
        var formatted = Text(text, size, brush, FontWeight.Normal);
        context.DrawText(formatted, new Point(
            (bounds.Width - formatted.Width) / 2,
            (bounds.Height - formatted.Height) / 2));
    }
}
