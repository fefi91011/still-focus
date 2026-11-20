using Avalonia.Media;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class AppUsageRowViewModel
{
    public string Name { get; init; } = string.Empty;
    public string DurationText { get; init; } = string.Empty;
    public double Fraction { get; init; }
    public IBrush BarBrush { get; init; } = UiBrushes.Accent;
}

/// <summary>
/// A read-only daily or weekly ranking of foreground application time. Sections
/// are rebuilt when their underlying data changes, so the view model stays simple.
/// </summary>
public sealed class AppUsageSectionViewModel
{
    public string Title { get; init; } = string.Empty;
    public string TotalText { get; init; } = "0 Min";
    public string EmptyText { get; init; } = "Noch keine App-Zeit erfasst.";
    public string MoreText { get; init; } = string.Empty;
    public IReadOnlyList<AppUsageRowViewModel> Rows { get; init; } = Array.Empty<AppUsageRowViewModel>();
    public bool IsEmpty => Rows.Count == 0;
    public bool HasMore => !string.IsNullOrWhiteSpace(MoreText);

    public static AppUsageSectionViewModel Create(
        IEnumerable<AppUsageEntry> entries,
        DateOnly from,
        DateOnly to,
        string title,
        int maxRows,
        string emptyText)
    {
        var grouped = entries
            .Where(entry => entry.Date >= from && entry.Date <= to && entry.UsedSeconds > 0)
            .GroupBy(entry => Target.NormalizeProcess(entry.ProcessName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .Select(group => new
            {
                Process = group.Key,
                Seconds = group.Sum(entry => entry.UsedSeconds)
            })
            .OrderByDescending(item => item.Seconds)
            .ThenBy(item => item.Process, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalSeconds = grouped.Sum(item => item.Seconds);
        var maxSeconds = grouped.Count == 0 ? 0 : grouped[0].Seconds;
        var visible = maxRows > 0 ? grouped.Take(maxRows).ToList() : grouped;
        var rows = visible
            .Select(item => new AppUsageRowViewModel
            {
                Name = FriendlyName(item.Process),
                DurationText = FormatDuration(item.Seconds),
                Fraction = maxSeconds > 0 ? (double)item.Seconds / maxSeconds : 0
            })
            .ToList();

        return new AppUsageSectionViewModel
        {
            Title = title,
            TotalText = FormatDuration(totalSeconds),
            EmptyText = emptyText,
            Rows = rows,
            MoreText = grouped.Count > visible.Count
                ? $"… und {grouped.Count - visible.Count} weitere Apps"
                : string.Empty
        };
    }

    public static string FormatDuration(int seconds)
    {
        seconds = Math.Max(0, seconds);
        if (seconds == 0)
        {
            return "0 Min";
        }

        if (seconds < 60)
        {
            return "<1 Min";
        }

        var minutes = (int)Math.Round(seconds / 60.0);
        return minutes < 60
            ? $"{minutes} Min"
            : $"{minutes / 60}:{minutes % 60:00} h";
    }

    public static string FriendlyName(string processName)
    {
        var name = Target.NormalizeProcess(processName);
        return name switch
        {
            "chrome" => "Google Chrome",
            "msedge" => "Microsoft Edge",
            "firefox" => "Firefox",
            "brave" => "Brave",
            "opera" => "Opera",
            "vivaldi" => "Vivaldi",
            "arc" => "Arc",
            "zen" => "Zen",
            "explorer" => "Explorer",
            "code" => "VS Code",
            "devenv" => "Visual Studio",
            "discord" => "Discord",
            "steam" => "Steam",
            "steamwebhelper" => "Steam",
            "spotify" => "Spotify",
            "slack" => "Slack",
            "teams" => "Microsoft Teams",
            "ms-teams" => "Microsoft Teams",
            "winword" => "Word",
            "excel" => "Excel",
            "powerpnt" => "PowerPoint",
            "notion" => "Notion",
            "telegram" => "Telegram",
            "whatsapp" => "WhatsApp",
            "zoom" => "Zoom",
            "epicgameslauncher" => "Epic Games",
            "riotclientservices" => "Riot Client",
            "applicationframehost" => "Windows-App",
            "searchhost" => "Windows-Suche",
            "shellexperiencehost" => "Windows-Shell",
            "still" or "still.app" => "Still",
            _ => name.Length == 0
                ? "Unbekannte App"
                : char.ToUpperInvariant(name[0]) + name[1..]
        };
    }
}
