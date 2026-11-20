using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Still.Core.Models;

namespace Still.App.Controls;

public sealed class ScheduleBlock
{
    public int DayIndex { get; init; }
    public double StartHour { get; init; }
    public double EndHour { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#C4F5A9";
}

public sealed class WeekGrid : Control
{
    private static readonly IBrush GridBrush = Brush.Parse("#1C222B");
    private static readonly IBrush LabelBrush = Brush.Parse("#6E7888");
    private static readonly IBrush TextBrush = Brush.Parse("#0E140C");
    private static readonly IBrush NowBrush = Brush.Parse("#F08A82");

    public static readonly StyledProperty<IReadOnlyList<ScheduleBlock>?> BlocksProperty =
        AvaloniaProperty.Register<WeekGrid, IReadOnlyList<ScheduleBlock>?>(nameof(Blocks));

    static WeekGrid()
    {
        AffectsRender<WeekGrid>(BlocksProperty);
    }

    public IReadOnlyList<ScheduleBlock>? Blocks
    {
        get => GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        const double gutter = 42;
        const double header = 28;
        const double footer = 4;

        if (Bounds.Width <= gutter + 40 || Bounds.Height <= header + 60)
        {
            return;
        }

        var gridWidth = Bounds.Width - gutter;
        var gridHeight = Bounds.Height - header - footer;
        var dayWidth = gridWidth / 7;

        for (var day = 0; day < 7; day++)
        {
            var x = gutter + day * dayWidth;
            var isToday = (int)DateTime.Now.DayOfWeek == (day + 1) % 7;

            if (isToday)
            {
                context.DrawRectangle(
                    Brush.Parse("#0F1318"),
                    null,
                    new Rect(x + 2, header - 6, dayWidth - 4, gridHeight + 6));
            }

            var label = Text(ShortDay(day), 10.5, isToday ? Brush.Parse("#C4F5A9") : LabelBrush,
                isToday ? FontWeight.SemiBold : FontWeight.Normal);
            context.DrawText(label, new Point(x + (dayWidth - label.Width) / 2, 4));

            if (day > 0)
            {
                context.DrawLine(new Pen(GridBrush, 1), new Point(x, header - 2), new Point(x, header + gridHeight));
            }
        }

        for (var hour = 0; hour <= 24; hour += 3)
        {
            var y = header + hour / 24.0 * gridHeight;
            context.DrawLine(new Pen(GridBrush, 1), new Point(gutter, y), new Point(Bounds.Width, y));

            if (hour < 24)
            {
                var label = Text($"{hour:00}:00", 9.5, LabelBrush, FontWeight.Normal);
                context.DrawText(label, new Point(4, y - label.Height / 2));
            }
        }

        var blocks = Blocks;
        if (blocks is not null)
        {
            foreach (var block in blocks)
            {
                if (block.DayIndex is < 0 or > 6)
                {
                    continue;
                }

                var start = Math.Clamp(block.StartHour, 0, 24);
                var end = Math.Clamp(block.EndHour, 0, 24);
                if (end <= start)
                {
                    continue;
                }

                var x = gutter + block.DayIndex * dayWidth + 4;
                var y = header + start / 24.0 * gridHeight;
                var height = Math.Max(3, (end - start) / 24.0 * gridHeight - 3);
                var rect = new Rect(x, y + 1, Math.Max(6, dayWidth - 8), height);
                var brush = Brush.Parse(block.ColorHex);
                context.DrawRectangle(brush, null, rect, 5, 5);

                if (height >= 22 && !string.IsNullOrWhiteSpace(block.Title))
                {
                    var text = new FormattedText(
                        block.Title,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
                        10,
                        TextBrush)
                    {
                        MaxTextWidth = Math.Max(1, rect.Width - 8),
                        Trimming = TextTrimming.CharacterEllipsis
                    };

                    context.DrawText(text, new Point(rect.X + 5, rect.Y + 4));
                }
            }
        }

        var now = DateTime.Now;
        var todayIndex = ((int)now.DayOfWeek + 6) % 7;
        var nowY = header + (now.Hour + now.Minute / 60.0) / 24.0 * gridHeight;
        var nowX = gutter + todayIndex * dayWidth;
        context.DrawLine(new Pen(NowBrush, 1.2), new Point(nowX, nowY), new Point(nowX + dayWidth, nowY));
        context.DrawEllipse(NowBrush, null, new Point(nowX + 4, nowY), 3, 3);
    }

    private static string ShortDay(int dayIndex) => dayIndex switch
    {
        0 => "Mo",
        1 => "Di",
        2 => "Mi",
        3 => "Do",
        4 => "Fr",
        5 => "Sa",
        _ => "So"
    };

    private static FormattedText Text(string text, double size, IBrush brush, FontWeight weight) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
}
