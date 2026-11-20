namespace Still.Core.Models;

public enum ScheduleMode
{
    Always,
    Weekly
}

/// <summary>An active window on one weekday. End may be before start, meaning the window crosses midnight.</summary>
public sealed class TimeWindow
{
    public DayOfWeek Day { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }

    public TimeWindow()
    {
    }

    public TimeWindow(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        Day = day;
        Start = start;
        End = end;
    }

    public TimeSpan Duration
    {
        get
        {
            if (End == Start)
            {
                return TimeSpan.FromDays(1);
            }

            if (End > Start)
            {
                return End.ToTimeSpan() - Start.ToTimeSpan();
            }

            return TimeSpan.FromDays(1) - (Start.ToTimeSpan() - End.ToTimeSpan());
        }
    }

    public TimeWindow Clone() => new(Day, Start, End);
}

public sealed class Schedule
{
    public ScheduleMode Mode { get; set; } = ScheduleMode.Always;
    public List<TimeWindow> Windows { get; set; } = new();

    public static Schedule Always() => new() { Mode = ScheduleMode.Always };

    public bool IsConfigured => Mode == ScheduleMode.Always || Windows.Count > 0;

    public bool IsActiveAt(DateTimeOffset now)
    {
        if (Mode == ScheduleMode.Always)
        {
            return true;
        }

        if (Windows.Count == 0)
        {
            return false;
        }

        var local = now.LocalDateTime;
        for (var offset = -1; offset <= 0; offset++)
        {
            var date = local.Date.AddDays(offset);
            foreach (var window in Windows)
            {
                if (window.Day != date.DayOfWeek)
                {
                    continue;
                }

                var start = date + window.Start.ToTimeSpan();
                var end = start + window.Duration;
                if (local >= start && local < end)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the next window start after <paramref name="now"/> and, when a window is
    /// currently active, the end of that window.
    /// </summary>
    public (DateTimeOffset? StartsAt, DateTimeOffset? EndsAt) NextTransition(DateTimeOffset now)
    {
        if (Mode == ScheduleMode.Always)
        {
            return (null, null);
        }

        var localNow = now.LocalDateTime;
        DateTimeOffset? nextStart = null;
        DateTimeOffset? activeEnd = null;

        for (var offset = -1; offset <= 8; offset++)
        {
            var date = localNow.Date.AddDays(offset);
            foreach (var window in Windows)
            {
                if (window.Day != date.DayOfWeek)
                {
                    continue;
                }

                var startLocal = date + window.Start.ToTimeSpan();
                var endLocal = startLocal + window.Duration;
                var start = ToOffset(startLocal);
                var end = ToOffset(endLocal);

                if (localNow >= startLocal && localNow < endLocal)
                {
                    if (activeEnd is null || end < activeEnd)
                    {
                        activeEnd = end;
                    }
                }
                else if (startLocal > localNow && (nextStart is null || start < nextStart))
                {
                    nextStart = start;
                }
            }
        }

        return (nextStart, activeEnd);
    }

    public string Describe()
    {
        if (Mode == ScheduleMode.Always)
        {
            return "Immer";
        }

        if (Windows.Count == 0)
        {
            return "Nie";
        }

        var groups = Windows
            .GroupBy(w => (w.Start, w.End))
            .OrderBy(g => g.Min(w => ((int)w.Day + 6) % 7))
            .Select(g =>
            {
                var days = string.Join(", ", g
                    .Select(w => w.Day)
                    .Distinct()
                    .OrderBy(d => ((int)d + 6) % 7)
                    .Select(ShortDay));

                var duration = g.First().Duration;
                var endLabel = g.First().End == TimeOnly.MinValue || duration >= TimeSpan.FromDays(1)
                    ? "24:00"
                    : g.First().End.ToString("HH:mm");

                var startLabel = g.First().Start.ToString("HH:mm");
                var label = g.First().Duration >= TimeSpan.FromDays(1)
                    ? $"{days} · ganztägig"
                    : $"{days} · {startLabel}–{endLabel}";

                if (g.First().End <= g.First().Start && g.First().Start != TimeOnly.MinValue)
                {
                    label += " (+1)";
                }

                return label;
            });

        return string.Join("   ", groups);
    }

    public Schedule Clone() => new()
    {
        Mode = Mode,
        Windows = Windows.Select(w => w.Clone()).ToList()
    };

    public static string ShortDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Mo",
        DayOfWeek.Tuesday => "Di",
        DayOfWeek.Wednesday => "Mi",
        DayOfWeek.Thursday => "Do",
        DayOfWeek.Friday => "Fr",
        DayOfWeek.Saturday => "Sa",
        DayOfWeek.Sunday => "So",
        _ => "?"
    };

    private static DateTimeOffset ToOffset(DateTime local)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
