using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Still.App.Services;
using Still.Core.Models;

namespace Still.App.ViewModels;

/// <summary>
/// One app or website target in the overview. The icon is loaded in the
/// background and falls back to initials while loading or when the source has
/// no usable icon.
/// </summary>
public sealed class OverviewTargetViewModel : ObservableObject
{
    private readonly Target _target;
    private Bitmap? _icon;

    public OverviewTargetViewModel(Target target, string statusText, IBrush statusBrush, bool isBlocked, bool isExempt)
    {
        _target = target;
        Name = target.DisplayName;
        Detail = target.Kind == TargetKind.Website ? target.Value : target.Value + ".exe";
        StatusText = statusText;
        StatusBrush = statusBrush;
        IsBlocked = isBlocked;
        IsExempt = isExempt;
        _ = LoadIconAsync();
    }

    public TargetKind Kind => _target.Kind;
    public string Name { get; }
    public string Detail { get; }
    public string StatusText { get; }
    public IBrush StatusBrush { get; }
    public bool IsBlocked { get; }
    public bool IsExempt { get; }
    public string ToolTipText => $"{Name} · {Detail} · {StatusText}";

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

    private async Task LoadIconAsync()
    {
        try
        {
            var icon = _target.Kind == TargetKind.Website
                ? await WebsiteIconService.GetBitmapAsync(_target.Value)
                : await AppIconService.GetForProcessAsync(_target.Value);

            if (icon is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Icon = icon);
            }
        }
        catch (Exception)
        {
            // The initials fallback keeps the target readable without an icon.
        }
    }
}
