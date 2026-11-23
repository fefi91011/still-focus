using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Still.App.Services;
using Still.Core.Models;
using Still.Core.Presets;

namespace Still.App.ViewModels;

/// <summary>
/// The target half of the rule editor: installed programmes, categories, typed
/// domains with a live icon preview, and the resulting chips. Shared by rules
/// and allowances so both always behave the same.
/// </summary>
public sealed class TargetSelectionViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<string>? _reportError;
    private CancellationTokenSource? _previewCts;
    private Bitmap? _websiteIcon;
    private string _websiteInput = string.Empty;
    private string _websitePreviewTitle = string.Empty;
    private string _websitePreviewHint = string.Empty;
    private bool _includeSubdomains = true;
    private string _processInput = string.Empty;

    public TargetSelectionViewModel(Action changed, Action<string>? reportError = null)
    {
        _changed = changed;
        _reportError = reportError;

        Apps = new AppCatalogViewModel(IsProcessTarget, ToggleInstalledApp);
        Apps.Loaded += RebuildCategories;

        AddWebsiteCommand = new RelayCommand(AddWebsite);
        AddProcessCommand = new RelayCommand(AddProcess);
        ClearWebsiteCommand = new RelayCommand(() =>
        {
            WebsiteInput = string.Empty;
            ClearPreview();
        });

        BuildCategories();
    }

    public ObservableCollection<TargetChipViewModel> Targets { get; } = new();
    public ObservableCollection<CategoryChipViewModel> Categories { get; } = new();
    public AppCatalogViewModel Apps { get; }

    public ICommand AddWebsiteCommand { get; }
    public ICommand AddProcessCommand { get; }
    public ICommand ClearWebsiteCommand { get; }

    public string WebsiteInput
    {
        get => _websiteInput;
        set
        {
            if (!Set(ref _websiteInput, value))
            {
                return;
            }

            Raise(nameof(HasWebsiteInput));
            QueuePreview();
        }
    }

    public string ProcessInput
    {
        get => _processInput;
        set => Set(ref _processInput, value);
    }

    /// <summary>Scope for websites added from now on; each chip keeps its own setting.</summary>
    public bool IncludeSubdomains
    {
        get => _includeSubdomains;
        set
        {
            if (Set(ref _includeSubdomains, value))
            {
                Raise(nameof(ScopeHint));
            }
        }
    }

    public string ScopeHint => IncludeSubdomains
        ? "Subdomains werden im Browser mitgesperrt, z. B. m.youtube.com."
        : "Nur genau diese Domain wird gesperrt.";

    public Bitmap? WebsiteIcon
    {
        get => _websiteIcon;
        private set
        {
            if (Set(ref _websiteIcon, value))
            {
                Raise(nameof(HasWebsiteIcon));
            }
        }
    }

    public bool HasWebsiteIcon => WebsiteIcon is not null;
    public bool HasWebsiteInput => !string.IsNullOrWhiteSpace(WebsiteInput);

    public string WebsitePreviewTitle
    {
        get => _websitePreviewTitle;
        private set
        {
            if (Set(ref _websitePreviewTitle, value))
            {
                Raise(nameof(HasWebsitePreview));
                Raise(nameof(WebsiteInitial));
            }
        }
    }

    public bool HasWebsitePreview => !string.IsNullOrEmpty(WebsitePreviewTitle);

    public string WebsitePreviewHint
    {
        get => _websitePreviewHint;
        private set => Set(ref _websitePreviewHint, value);
    }

    public string WebsiteInitial =>
        string.IsNullOrEmpty(WebsitePreviewTitle) ? "?" : WebsitePreviewTitle[..1].ToUpperInvariant();


    public int AppCount => Targets.Count(t => t.Target.Kind == TargetKind.Process);
    public int WebsiteCount => Targets.Count(t => t.Target.Kind == TargetKind.Website);

    public void Load(IEnumerable<Target> targets)
    {
        ClearPreview();
        WebsiteInput = string.Empty;
        ProcessInput = string.Empty;
        IncludeSubdomains = true;

        Targets.Clear();
        foreach (var target in targets)
        {
            AddChip(target.Clone());
        }

        Apps.EnsureLoaded();
        RefreshAddedState();
        RefreshCategories();
        _changed();
    }

    public void ApplyCategory(TargetCategory category)
    {
        var chip = Categories.FirstOrDefault(c => c.Category.Key == category.Key);
        if (chip is null)
        {
            return;
        }

        chip.UpdateApplied();

        if (chip.IsApplied)
        {
            foreach (var target in chip.MatchedTargets.ToList())
            {
                var existing = Targets.FirstOrDefault(t => t.Target.SameAs(target));
                if (existing is not null)
                {
                    Targets.Remove(existing);
                }
            }
        }
        else
        {
            foreach (var target in chip.MatchedTargets)
            {
                if (Contains(target)
                    || Targets.Any(t => t.Target.Kind == target.Kind
                                        && t.Display.Equals(target.DisplayName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                AddChip(target.Clone());
            }
        }

        RefreshAddedState();
        RefreshCategories();
        _changed();
    }

    private bool Contains(Target target) => Targets.Any(t => t.Target.SameAs(target));

    private bool IsProcessTarget(string processName) =>
        Targets.Any(t => t.Target.Kind == TargetKind.Process
                         && t.Target.Value.Equals(processName, StringComparison.OrdinalIgnoreCase));

    private void ToggleInstalledApp(InstalledApplication app)
    {
        var existing = Targets.FirstOrDefault(t => t.Target.Kind == TargetKind.Process
                                                   && t.Target.Value.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Targets.Remove(existing);
        }
        else
        {
            AddChip(new Target(TargetKind.Process, app.ProcessName, app.Name));
        }

        RefreshAddedState();
        RefreshCategories();
        _changed();
    }

    private void AddWebsite()
    {
        var domain = Target.NormalizeHost(WebsiteInput);
        if (domain.Length == 0 || !domain.Contains('.') || domain.Contains(' '))
        {
            _reportError?.Invoke("Bitte gib eine Adresse ein, z. B. youtube.com.");
            return;
        }

        if (Contains(new Target(TargetKind.Website, domain)))
        {
            _reportError?.Invoke($"{domain} ist bereits in dieser Regel.");
            return;
        }

        var chip = AddChip(new Target(TargetKind.Website, domain) { IncludeSubdomains = IncludeSubdomains });
        LoadChipIcon(chip);

        WebsiteInput = string.Empty;
        ClearPreview();
        RefreshCategories();
        _changed();
    }

    private void AddProcess()
    {
        var process = Target.NormalizeProcess(ProcessInput);
        if (process.Length == 0)
        {
            _reportError?.Invoke("Bitte gib einen Prozessnamen ein, z. B. discord.");
            return;
        }

        if (IsProcessTarget(process))
        {
            _reportError?.Invoke($"{process} ist bereits in dieser Regel.");
            return;
        }

        AddChip(new Target(TargetKind.Process, process));
        ProcessInput = string.Empty;
        RefreshAddedState();
        RefreshCategories();
        _changed();
    }

    private TargetChipViewModel AddChip(Target target)
    {
        var chip = new TargetChipViewModel(target, RemoveChip, _changed, Apps.FindPathForProcess);
        Targets.Add(chip);

        if (chip.IsWebsite)
        {
            LoadChipIcon(chip);
        }

        return chip;
    }

    private void RemoveChip(TargetChipViewModel chip)
    {
        Targets.Remove(chip);
        RefreshAddedState();
        RefreshCategories();
        _changed();
    }

    private void BuildCategories()
    {
        Categories.Clear();
        foreach (var category in TargetCategories.All)
        {
            Categories.Add(new CategoryChipViewModel(category, Apps.AllApps, Contains, ApplyCategory));
        }
    }

    private void RebuildCategories()
    {
        BuildCategories();
        RefreshCategories();
        RefreshAddedState();
    }

    private void RefreshCategories()
    {
        foreach (var chip in Categories)
        {
            chip.UpdateApplied();
        }
    }

    private void RefreshAddedState() => Apps.RefreshAdded();

    private void QueuePreview()
    {
        CancelPreview();

        var domain = Target.NormalizeHost(WebsiteInput);
        if (domain.Length == 0 || !domain.Contains('.') || domain.Contains(' '))
        {
            ClearPreview();
            return;
        }

        WebsitePreviewTitle = domain;
        WebsitePreviewHint = "Icon wird geladen …";
        WebsiteIcon = null;

        var cts = new CancellationTokenSource();
        _previewCts = cts;
        _ = LoadPreviewAsync(domain, cts);
    }

    private async Task LoadPreviewAsync(string domain, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(350, cts.Token);
            var payloads = await WebsiteIconService.GetAsync(domain);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var icon = DecodeFirst(payloads);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cts.IsCancellationRequested
                    || !Target.NormalizeHost(WebsiteInput).Equals(domain, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var bitmap = icon;
                WebsiteIcon = bitmap;
                WebsitePreviewHint = bitmap is null
                    ? "Kein Icon gefunden – die Seite liefert keins."
                    : "Icon gefunden";
                Raise(nameof(WebsiteInitial));
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // A failed preview is a hint, never an error in the editor.
        }
    }

    private void LoadChipIcon(TargetChipViewModel chip)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var payloads = await WebsiteIconService.GetAsync(chip.Target.Value);
                var bitmap = DecodeFirst(payloads);
                if (bitmap is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => chip.SetIcon(bitmap));
                }
            }
            catch (Exception)
            {
            }
        });
    }

    private static Bitmap? DecodeFirst(IReadOnlyList<byte[]> payloads)
    {
        foreach (var bytes in payloads)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch (Exception)
            {
                // Try the next candidate; an ICO may not be decodable here.
            }
        }

        return null;
    }

    private void ClearPreview()
    {
        WebsiteIcon = null;
        WebsitePreviewTitle = string.Empty;
        WebsitePreviewHint = string.Empty;
    }

    private void CancelPreview()
    {
        if (_previewCts is null)
        {
            return;
        }

        _previewCts.Cancel();
        _previewCts = null;
    }
}

public sealed class TargetChipViewModel : ObservableObject
{
    private Bitmap? _icon;

    public TargetChipViewModel(
        Target target,
        Action<TargetChipViewModel> remove,
        Action? changed = null,
        Func<string, string?>? resolveAppPath = null)
    {
        Target = target;
        RemoveCommand = new RelayCommand(() => remove(this));
        ToggleScopeCommand = new RelayCommand(() =>
        {
            Target.IncludeSubdomains = !Target.IncludeSubdomains;
            Raise(nameof(ScopeLabel));
            changed?.Invoke();
        });

        if (Target.Kind == TargetKind.Process && resolveAppPath?.Invoke(Target.Value) is { Length: > 0 } path)
        {
            _ = LoadAppIconAsync(path);
        }
    }

    private async Task LoadAppIconAsync(string path)
    {
        try
        {
            var icon = await AppIconService.GetBitmapAsync(path);
            if (icon is not null)
            {
                Icon = icon;
            }
        }
        catch (Exception)
        {
        }
    }

    public Target Target { get; }
    public string Display => string.IsNullOrWhiteSpace(Target.Label) ? Target.Value : Target.Label!;
    public string KindLabel => Target.Kind == TargetKind.Website ? "WEB" : "APP";
    public bool IsWebsite => Target.Kind == TargetKind.Website;
    public string ScopeLabel => Target.IncludeSubdomains ? "inkl. Subdomains" : "nur Domain";
    public string ToolTipText => Target.Kind == TargetKind.Website ? Target.Value : Target.Value + ".exe";
    public ICommand RemoveCommand { get; }
    public ICommand ToggleScopeCommand { get; }

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

    internal void SetIcon(Bitmap bitmap) => Icon = bitmap;
}

public sealed class CategoryChipViewModel : ObservableObject
{
    private readonly Func<Target, bool> _contains;
    private readonly Action<TargetCategory> _apply;
    private bool _isApplied;

    public CategoryChipViewModel(
        TargetCategory category,
        IReadOnlyList<InstalledApplication> apps,
        Func<Target, bool> contains,
        Action<TargetCategory> apply)
    {
        Category = category;
        _contains = contains;
        _apply = apply;

        MatchedTargets = apps
            .Where(app => category.MatchesApp(app.Name, app.ProcessName, app.Path))
            .Select(app => new Target(TargetKind.Process, app.ProcessName, app.Name))
            .Concat(category.Websites.Select(site => new Target(TargetKind.Website, site)))
            .ToList();

        ApplyCommand = new RelayCommand(() => _apply(Category));
        UpdateApplied();
    }

    public TargetCategory Category { get; }
    public string Name => Category.Name;
    public string Description => Category.Description;
    public IReadOnlyList<Target> MatchedTargets { get; }
    public ICommand ApplyCommand { get; }

    public int AppCount => MatchedTargets.Count(t => t.Kind == TargetKind.Process);
    public int WebsiteCount => MatchedTargets.Count(t => t.Kind == TargetKind.Website);
    public bool HasMatches => MatchedTargets.Count > 0;

    public string CountLabel => MatchedTargets.Count == 0
        ? "nichts gefunden"
        : string.Join(" · ", new[]
        {
            AppCount > 0 ? (AppCount == 1 ? "1 Programm" : $"{AppCount} Programme") : null,
            WebsiteCount > 0 ? (WebsiteCount == 1 ? "1 Webseite" : $"{WebsiteCount} Webseiten") : null
        }.Where(part => part is not null));

    public bool IsApplied
    {
        get => _isApplied;
        private set => Set(ref _isApplied, value);
    }

    public void UpdateApplied()
    {
        IsApplied = MatchedTargets.Count > 0 && MatchedTargets.All(_contains);
    }
}

public sealed class AppCatalogViewModel : ObservableObject
{
    private readonly Func<string, bool> _isAdded;
    private readonly Action<InstalledApplication> _toggle;
    private IReadOnlyList<InstalledApplication> _all = Array.Empty<InstalledApplication>();
    private bool _loaded;
    private bool _loading;
    private string _search = string.Empty;
    private string _status = "Programme werden gesucht …";
    private SourceFilterViewModel _selected;

    public AppCatalogViewModel(Func<string, bool> isAdded, Action<InstalledApplication> toggle)
    {
        _isAdded = isAdded;
        _toggle = toggle;

        Sources =
        [
            new SourceFilterViewModel("Alle", null, this),
            new SourceFilterViewModel("Programme", InstalledAppScanner.SourcePrograms, this),
            new SourceFilterViewModel("x86", InstalledAppScanner.SourceProgramsX86, this),
            new SourceFilterViewModel("Benutzer", InstalledAppScanner.SourceUserPrograms, this)
        ];

        _selected = Sources[0];
        _selected.IsSelected = true;
    }

    public event Action? Loaded;

    public ObservableCollection<AppOptionViewModel> Apps { get; } = new();
    public ObservableCollection<SourceFilterViewModel> Sources { get; }
    public IReadOnlyList<InstalledApplication> AllApps => _all;

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                Rebuild();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsScanning => _loading;

    public void EnsureLoaded()
    {
        if (_loaded || _loading)
        {
            return;
        }

        _ = LoadAsync();
    }

    public void Select(SourceFilterViewModel filter)
    {
        _selected = filter;
        foreach (var source in Sources)
        {
            source.IsSelected = source == filter;
        }

        Rebuild();
    }

    public void RefreshAdded()
    {
        foreach (var option in Apps)
        {
            option.IsAdded = _isAdded(option.ProcessName);
        }
    }

    public string? FindPathForProcess(string processName) =>
        _all.FirstOrDefault(app => app.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))?.Path;

    internal void Toggle(AppOptionViewModel option)
    {
        if (option.Application is { } app)
        {
            _toggle(app);
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        Raise(nameof(IsScanning));
        try
        {
            _all = await Task.Run(InstalledAppScanner.Scan);
            _loaded = true;
            foreach (var source in Sources)
            {
                source.Count = source.Source is null
                    ? _all.Count
                    : _all.Count(app => app.Source == source.Source);
            }

            Rebuild();
            Loaded?.Invoke();
        }
        catch (Exception ex)
        {
            Status = "Programme konnten nicht gelesen werden: " + ex.Message;
        }
        finally
        {
            _loading = false;
            Raise(nameof(IsScanning));
        }
    }

    private void Rebuild()
    {
        Apps.Clear();
        if (!_loaded)
        {
            return;
        }

        var query = Search.Trim();
        var matches = _all
            .Where(app => _selected.Source is null || app.Source == _selected.Source)
            .Where(app => query.Length == 0
                          || app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || app.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || app.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || (app.Publisher?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        foreach (var app in matches)
        {
            Apps.Add(new AppOptionViewModel(app, _isAdded(app.ProcessName), this));
        }

        Status = _all.Count == 0
            ? "Keine Programme gefunden."
            : matches.Count == 0
                ? "Kein Programm passt zur Suche."
                : $"{matches.Count} von {_all.Count} Programmen";
    }
}

public sealed class AppOptionViewModel : ObservableObject
{
    private readonly AppCatalogViewModel _owner;
    private bool _isAdded;

    public AppOptionViewModel(InstalledApplication application, bool isAdded, AppCatalogViewModel owner)
    {
        Application = application;
        _owner = owner;
        _isAdded = isAdded;
        ToggleCommand = new RelayCommand(() => _owner.Toggle(this));
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        try
        {
            Icon = await AppIconService.GetBitmapAsync(Path);
        }
        catch (Exception)
        {
        }
    }

    private Bitmap? _icon;

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

    public InstalledApplication Application { get; }
    public string Name => Application.Name;
    public string ProcessName => Application.ProcessName;
    public string Source => Application.Source;
    public string Path => Application.Path;
    public string ToolTipText => Application.Path;
    public ICommand ToggleCommand { get; }

    public string Initials
    {
        get
        {
            var words = Application.Name
                .Split([' ', '-', '.', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .ToList();
            return words.Count == 0
                ? "?"
                : string.Concat(words.Select(word => char.ToUpperInvariant(word[0])));
        }
    }

    public string Meta => string.IsNullOrWhiteSpace(Application.Publisher)
        ? $"{Application.Source} · {Application.ProcessName}.exe"
        : $"{Application.Source} · {Application.Publisher}";

    public string AddLabel => IsAdded ? "✓" : "+";

    public bool IsAdded
    {
        get => _isAdded;
        set
        {
            if (Set(ref _isAdded, value))
            {
                Raise(nameof(AddLabel));
            }
        }
    }
}

public sealed class SourceFilterViewModel : ObservableObject
{
    private readonly AppCatalogViewModel _owner;
    private bool _isSelected;
    private int _count;

    public SourceFilterViewModel(string label, string? source, AppCatalogViewModel owner)
    {
        Label = label;
        Source = source;
        _owner = owner;
        Command = new RelayCommand(() => _owner.Select(this));
    }

    public string Label { get; }
    public string? Source { get; }
    public ICommand Command { get; }

    public int Count
    {
        get => _count;
        set => Set(ref _count, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}
