using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Still.App.Services;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class WebsiteUsageRowViewModel : ObservableObject
{
    private Bitmap? _icon;

    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string DurationText { get; init; } = string.Empty;
    public double Fraction { get; init; }
    public IBrush BarBrush { get; init; } = UiBrushes.Info;

    public bool ShowHost => !Name.Equals(Host, StringComparison.OrdinalIgnoreCase);

    public Bitmap? Icon
    {
        get => _icon;
        private set
        {
            if (Set(ref _icon, value))
            {
                Raise(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => Icon is not null;

    public string Initials
    {
        get
        {
            var words = Name.Split([' ', '-', '.', '_'], StringSplitOptions.RemoveEmptyEntries).Take(2).ToList();
            return words.Count == 0
                ? "?"
                : string.Concat(words.Select(word => char.ToUpperInvariant(word[0])));
        }
    }

    internal void SetIcon(Bitmap icon) => Icon = icon;
}

/// <summary>
/// A read-only daily or weekly ranking of active website time. The data comes
/// from the browser extension, so it only covers HTTP(S) tabs in the focused
/// browser window.
/// </summary>
public sealed class WebsiteUsageSectionViewModel
{
    public string Title { get; init; } = string.Empty;
    public string TotalText { get; init; } = "0 Min";
    public string EmptyText { get; init; } = "Noch keine Website-Zeit erfasst.";
    public string MoreText { get; init; } = string.Empty;
    public IReadOnlyList<WebsiteUsageRowViewModel> Rows { get; init; } = Array.Empty<WebsiteUsageRowViewModel>();
    public bool IsEmpty => Rows.Count == 0;
    public bool HasMore => !string.IsNullOrWhiteSpace(MoreText);

    public static WebsiteUsageSectionViewModel Create(
        IEnumerable<WebsiteUsageEntry> entries,
        DateOnly from,
        DateOnly to,
        string title,
        int maxRows,
        string emptyText)
    {
        var grouped = entries
            .Where(entry => entry.Date >= from && entry.Date <= to && entry.UsedSeconds > 0)
            .GroupBy(entry => GroupHost(entry.Host), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .Select(group => new
            {
                Host = group.Key,
                Seconds = group.Sum(entry => entry.UsedSeconds)
            })
            .OrderByDescending(item => item.Seconds)
            .ThenBy(item => item.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalSeconds = grouped.Sum(item => item.Seconds);
        var maxSeconds = grouped.Count == 0 ? 0 : grouped[0].Seconds;
        var visible = maxRows > 0 ? grouped.Take(maxRows).ToList() : grouped;
        var rows = visible
            .Select(item => new WebsiteUsageRowViewModel
            {
                Name = FriendlyName(item.Host),
                Host = item.Host,
                DurationText = AppUsageSectionViewModel.FormatDuration(item.Seconds),
                Fraction = maxSeconds > 0 ? (double)item.Seconds / maxSeconds : 0
            })
            .ToList();

        _ = LoadIconsAsync(rows);

        return new WebsiteUsageSectionViewModel
        {
            Title = title,
            TotalText = AppUsageSectionViewModel.FormatDuration(totalSeconds),
            EmptyText = emptyText,
            Rows = rows,
            MoreText = grouped.Count > visible.Count
                ? $"… und {grouped.Count - visible.Count} weitere Websites"
                : string.Empty
        };
    }

    private static async Task LoadIconsAsync(IReadOnlyList<WebsiteUsageRowViewModel> rows)
    {
        try
        {
            foreach (var row in rows)
            {
                var icon = await WebsiteIconService.GetBitmapAsync(row.Host);
                if (icon is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => row.SetIcon(icon));
                }
            }
        }
        catch (Exception)
        {
            // Website icons are a visual nicety; the host and time always remain.
        }
    }

    public static string FriendlyName(string host)
    {
        var normalized = Target.NormalizeHost(host);
        if (normalized.Length == 0)
        {
            return "Unbekannte Website";
        }

        foreach (var (domain, name) in KnownSites)
        {
            if (normalized.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return normalized;
    }

    private static string GroupHost(string host)
    {
        var normalized = Target.NormalizeHost(host);
        foreach (var (domain, _) in KnownSites)
        {
            if (normalized.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return domain;
            }
        }

        return normalized;
    }

    private static readonly (string Domain, string Name)[] KnownSites =
    [
        ("youtube.com", "YouTube"),
        ("youtu.be", "YouTube"),
        ("reddit.com", "Reddit"),
        ("x.com", "X"),
        ("twitter.com", "X"),
        ("instagram.com", "Instagram"),
        ("tiktok.com", "TikTok"),
        ("facebook.com", "Facebook"),
        ("twitch.tv", "Twitch"),
        ("netflix.com", "Netflix"),
        ("spotify.com", "Spotify"),
        ("github.com", "GitHub"),
        ("stackoverflow.com", "Stack Overflow"),
        ("wikipedia.org", "Wikipedia"),
        ("google.com", "Google"),
        ("amazon.de", "Amazon"),
        ("amazon.com", "Amazon"),
        ("nzz.ch", "NZZ"),
        ("spiegel.de", "Spiegel"),
        ("linkedin.com", "LinkedIn"),
        ("discord.com", "Discord"),
        ("whatsapp.com", "WhatsApp"),
        ("telegram.org", "Telegram"),
        ("microsoft.com", "Microsoft"),
        ("apple.com", "Apple")
    ];
}
