using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Still.App.Services;
using Still.Core.Models;
using Still.Core.Presets;

namespace Still.App.ViewModels;

public sealed class RulesViewModel : ObservableObject, IPageViewModel
{
    private readonly MainViewModel _main;
    private string _signature = "init";
    private bool _isRulesTab = true;
    private bool _isAllowancesTab;

    public RulesViewModel(MainViewModel main)
    {
        _main = main;
        Editor = new RuleEditorViewModel(this, main);
        AllowanceEditor = new AllowanceEditorViewModel(this, main);

        NewRuleCommand = new RelayCommand(OpenNewRule);
        NewAllowanceCommand = new RelayCommand(OpenNewAllowance);
        ShowRulesTabCommand = new RelayCommand(() =>
        {
            IsRulesTab = true;
            IsAllowancesTab = false;
        });
        ShowAllowancesTabCommand = new RelayCommand(() =>
        {
            IsRulesTab = false;
            IsAllowancesTab = true;
        });

        Refresh();
    }

    public ObservableCollection<RuleRowViewModel> Rules { get; } = new();
    public ObservableCollection<AllowanceRowViewModel> Allowances { get; } = new();
    public RuleEditorViewModel Editor { get; }
    public AllowanceEditorViewModel AllowanceEditor { get; }

    public ICommand NewRuleCommand { get; }
    public ICommand NewAllowanceCommand { get; }
    public ICommand ShowRulesTabCommand { get; }
    public ICommand ShowAllowancesTabCommand { get; }

    public bool IsRulesTab
    {
        get => _isRulesTab;
        private set => Set(ref _isRulesTab, value);
    }

    public bool IsAllowancesTab
    {
        get => _isAllowancesTab;
        private set => Set(ref _isAllowancesTab, value);
    }

    public bool HasRules => Rules.Count > 0;
    public bool HasAllowances => Allowances.Count > 0;
    public string RulesCountText => Rules.Count == 1 ? "1 Regel" : $"{Rules.Count} Regeln";
    public string AllowancesCountText => Allowances.Count == 1 ? "1 Ausnahme" : $"{Allowances.Count} Ausnahmen";

    public void Refresh()
    {
        var state = _main.Runtime.State;
        var snapshot = _main.Runtime.Snapshot;
        var now = DateTimeOffset.Now;

        var sig = string.Join("|", state.Rules.Select(r =>
                       $"{r.Id}:{r.Name}:{r.Enabled}:{r.Targets.Count}:{r.Schedule.Describe()}:{r.DailyLimitMinutes}"))
                  + "§" + string.Join("|", snapshot.Rules.Select(r =>
                       $"{r.RuleId}:{r.Blocked}:{r.Armed}:{r.UsedMinutes}:{r.RemainingMinutes}:{r.GraceUses}"))
                  + "§" + string.Join("|", state.Allowances.Select(a =>
                       $"{a.Id}:{a.Name}:{a.Enabled}:{a.IsActiveAt(now)}:{a.Targets.Count}:{a.ExpiresAt}"))
                  + $"§{Editor.IsOpen}:{Editor.Id}§{AllowanceEditor.IsOpen}:{AllowanceEditor.Id}";

        if (sig == _signature)
        {
            return;
        }

        _signature = sig;

        Rules.Clear();
        foreach (var rule in state.Rules.OrderByDescending(r => r.Enabled).ThenBy(r => r.Name))
        {
            var status = snapshot.FindRule(rule.Id);
            var limit = rule.DailyLimitMinutes is int value && value > 0 ? value : (int?)null;
            var blocked = status?.Blocked == true;
            var stateLabel = !rule.Enabled
                ? "Pausiert"
                : blocked
                    ? "Gesperrt"
                    : status?.Armed == true ? "Aktiv" : "Geplant";

            var targets = rule.DistinctTargets()
                .Select(t => t.Label ?? t.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var targetText = string.Join(" · ", targets.Take(4));
            if (targets.Count > 4)
            {
                targetText += $" +{targets.Count - 4}";
            }

            var budget = limit is null
                ? "Ohne Limit"
                : $"{status?.UsedMinutes ?? 0} / {limit + (status?.BonusMinutes ?? 0) + (status?.GraceBonusMinutes ?? 0)} Min";

            Rules.Add(new RuleRowViewModel
            {
                Id = rule.Id,
                Name = rule.Name,
                TargetsText = targetText,
                ScheduleText = rule.Schedule.Describe(),
                LimitText = budget,
                StateLabel = stateLabel,
                IsPaused = !rule.Enabled,
                IsBlocked = blocked,
                IsSelected = Editor.IsOpen && Editor.Id == rule.Id,
                OpenCommand = new RelayCommand(() => OpenRule(rule.Id))
            });
        }

        Allowances.Clear();
        foreach (var allowance in state.Allowances.OrderByDescending(a => a.Enabled).ThenBy(a => a.Name))
        {
            var active = allowance.IsActiveAt(now);
            var availability = !allowance.Enabled
                ? "Pausiert"
                : active
                    ? allowance.ExpiresAt is null ? "Jetzt aktiv" : $"Aktiv bis {allowance.ExpiresAt.Value.LocalDateTime:dd.MM.}"
                    : allowance.ExpiresAt is null ? "Gerade nicht aktiv" : $"Bis {allowance.ExpiresAt.Value.LocalDateTime:dd.MM.}";

            Allowances.Add(new AllowanceRowViewModel
            {
                Id = allowance.Id,
                Name = allowance.Name,
                TargetsText = string.Join(" · ", allowance.Targets.Select(t => t.Label ?? t.Value).Take(4)),
                AvailabilityText = availability,
                IsActive = active,
                IsPaused = !allowance.Enabled,
                IsSelected = AllowanceEditor.IsOpen && AllowanceEditor.Id == allowance.Id,
                OpenCommand = new RelayCommand(() => OpenAllowance(allowance.Id))
            });
        }

        Raise(nameof(HasRules));
        Raise(nameof(HasAllowances));
        Raise(nameof(RulesCountText));
        Raise(nameof(AllowancesCountText));
    }

    internal async Task SaveRuleAsync(BlockRule rule)
    {
        var approved = await _main.EnsureApprovalAsync("diese Regel");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.SaveRule(rule, approved: true);
        if (!result.Ok)
        {
            Editor.SetError(result.Message);
            return;
        }

        Editor.Close();
        _main.Notify(result);
        Refresh();
    }

    internal async Task DeleteRuleAsync(Guid ruleId, string name)
    {
        if (!await _main.ConfirmAsync("Regel löschen", $"„{name}“ wirklich löschen?", "Löschen"))
        {
            return;
        }

        var approved = await _main.EnsureApprovalAsync("das Löschen dieser Regel");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.DeleteRule(ruleId, approved: true);
        if (!result.Ok)
        {
            Editor.SetError(result.Message);
            return;
        }

        Editor.Close();
        _main.Notify(result);
        Refresh();
    }

    internal async Task SaveAllowanceAsync(Allowance allowance)
    {
        var approved = await _main.EnsureApprovalAsync("diese Ausnahme");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.SaveAllowance(allowance, approved: true);
        if (!result.Ok)
        {
            AllowanceEditor.SetError(result.Message);
            return;
        }

        AllowanceEditor.Close();
        _main.Notify(result);
        Refresh();
    }

    internal async Task DeleteAllowanceAsync(Guid allowanceId, string name)
    {
        if (!await _main.ConfirmAsync("Ausnahme löschen", $"„{name}“ wirklich löschen?", "Löschen"))
        {
            return;
        }

        var approved = await _main.EnsureApprovalAsync("das Löschen dieser Ausnahme");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.DeleteAllowance(allowanceId, approved: true);
        if (!result.Ok)
        {
            AllowanceEditor.SetError(result.Message);
            return;
        }

        AllowanceEditor.Close();
        _main.Notify(result);
        Refresh();
    }

    private void OpenNewRule()
    {
        IsRulesTab = true;
        IsAllowancesTab = false;
        var settings = _main.Runtime.State.Settings;
        Editor.Open(new BlockRule
        {
            Name = string.Empty,
            Enabled = true,
            Schedule = Schedule.Always(),
            DailyLimitMinutes = 60,
            GraceMinutes = settings.DefaultGraceMinutes,
            MaxGraceUsesPerDay = settings.DefaultMaxGraceUses
        }, isNew: true);
        Refresh();
    }

    private void OpenRule(Guid ruleId)
    {
        var rule = _main.Runtime.State.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
        {
            return;
        }

        Editor.Open(rule.Clone(), isNew: false);
        Refresh();
    }

    private void OpenNewAllowance()
    {
        IsRulesTab = false;
        IsAllowancesTab = true;
        AllowanceEditor.Open(new Allowance
        {
            Name = string.Empty,
            Enabled = true,
            Schedule = Schedule.Always()
        }, isNew: true);
        Refresh();
    }

    private void OpenAllowance(Guid allowanceId)
    {
        var allowance = _main.Runtime.State.Allowances.FirstOrDefault(a => a.Id == allowanceId);
        if (allowance is null)
        {
            return;
        }

        AllowanceEditor.Open(allowance.Clone(), isNew: false);
        Refresh();
    }
}

public sealed class RuleRowViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public string ScheduleText { get; init; } = string.Empty;
    public string LimitText { get; init; } = string.Empty;
    public string StateLabel { get; init; } = string.Empty;
    public bool IsPaused { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsSelected { get; init; }
    public ICommand OpenCommand { get; init; } = new RelayCommand(() => { });
}

public sealed class AllowanceRowViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public string AvailabilityText { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsPaused { get; init; }
    public bool IsSelected { get; init; }
    public ICommand OpenCommand { get; init; } = new RelayCommand(() => { });
}

public sealed class AppPickerViewModel : ObservableObject
{
    private readonly Action<InstalledApplication> _add;
    private IReadOnlyList<InstalledApplication> _all = Array.Empty<InstalledApplication>();
    private bool _isOpen;
    private bool _scanned;
    private bool _scanning;
    private string _search = string.Empty;
    private string _status = "Programme werden beim Öffnen aus dem Programme-Ordner gelesen.";

    public AppPickerViewModel(Action<InstalledApplication> add)
    {
        _add = add;
        ToggleCommand = new RelayCommand(() =>
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        });
        CloseCommand = new RelayCommand(Close);
    }

    public ObservableCollection<AppOptionViewModel> Apps { get; } = new();
    public ICommand ToggleCommand { get; }
    public ICommand CloseCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

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

    public void Open()
    {
        if (!IsOpen)
        {
            IsOpen = true;
        }

        if (_scanned)
        {
            Rebuild();
        }
        else
        {
            _ = LoadAsync();
        }
    }

    public void Close()
    {
        IsOpen = false;
        Search = string.Empty;
    }

    public void SetStatus(string message) => Status = message;

    private async Task LoadAsync()
    {
        if (_scanning)
        {
            return;
        }

        _scanning = true;
        Status = "Durchsuche Programme-Ordner …";
        try
        {
            _all = await Task.Run(InstalledAppScanner.Scan);
            _scanned = true;
            Rebuild();
        }
        catch (Exception ex)
        {
            Status = "Programme konnten nicht gelesen werden: " + ex.Message;
        }
        finally
        {
            _scanning = false;
        }
    }

    private void Rebuild()
    {
        Apps.Clear();
        if (!_scanned)
        {
            return;
        }

        var query = Search.Trim();
        var matches = _all
            .Where(app => query.Length == 0
                          || app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || app.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || app.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var app in matches)
        {
            Apps.Add(new AppOptionViewModel(app, _add));
        }

        Status = _all.Count == 0
            ? "Keine Programme im Programme-Ordner gefunden."
            : matches.Count == 0
                ? "Kein Programm passt zur Suche."
                : $"{matches.Count} Programme · tippe auf einen Eintrag, um ihn hinzuzufügen.";
    }
}

public sealed class AppOptionViewModel
{
    public AppOptionViewModel(InstalledApplication app, Action<InstalledApplication> add)
    {
        Name = app.Name;
        ProcessName = app.ProcessName;
        Path = app.Path;
        AddCommand = new RelayCommand(() => add(app));
    }

    public string Name { get; }
    public string ProcessName { get; }
    public string Path { get; }
    public string Detail => $"{ProcessName}.exe · {Path}";
    public ICommand AddCommand { get; }
}

public sealed class TargetChipViewModel
{
    public TargetChipViewModel(Target target, Action<TargetChipViewModel> remove)
    {
        Target = target;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public Target Target { get; }
    public string Display => string.IsNullOrWhiteSpace(Target.Label) ? Target.Value : Target.Label!;
    public string KindLabel => Target.Kind == TargetKind.Website ? "WEB" : "APP";
    public ICommand RemoveCommand { get; }
}

public sealed class DayToggleViewModel : ObservableObject
{
    private bool _isSelected;

    public DayToggleViewModel(DayOfWeek day, bool selected)
    {
        Day = day;
        _isSelected = selected;
        Label = Schedule.ShortDay(day);
    }

    public DayOfWeek Day { get; }
    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public sealed class RuleEditorViewModel : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly RulesViewModel _owner;
    private readonly MainViewModel _main;

    private bool _isOpen;
    private bool _isNew;
    private Guid _id;
    private DateTimeOffset _createdAt = DateTimeOffset.Now;
    private string _name = string.Empty;
    private string _note = string.Empty;
    private string _targetInput = string.Empty;
    private TargetPreset? _selectedPreset;
    private string _limitText = "60";
    private string _graceText = "5";
    private string _maxGraceText = "2";
    private string _startText = "09:00";
    private string _endText = "17:00";
    private bool _isAlways = true;
    private bool _enabled = true;
    private string? _errorText;
    private string _limitHint = string.Empty;

    public RuleEditorViewModel(RulesViewModel owner, MainViewModel main)
    {
        _owner = owner;
        _main = main;
        Presets = TargetPresets.All;

        Days = new ObservableCollection<DayToggleViewModel>(
            Enum.GetValues<DayOfWeek>().Select(d => new DayToggleViewModel(d, d is >= DayOfWeek.Monday and <= DayOfWeek.Friday)));

        AppPicker = new AppPickerViewModel(AddInstalledApp);
        AddWebsiteCommand = new RelayCommand(() => AddTarget(TargetKind.Website));
        AddAppCommand = new RelayCommand(() => AddTarget(TargetKind.Process));
        AddPresetCommand = new RelayCommand(AddPreset);
        CancelCommand = new RelayCommand(Close);
        SaveCommand = new AsyncRelayCommand(SaveAsync, ex => SetError(ex.Message));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, ex => SetError(ex.Message));
        UpdateLimitHint();
    }

    public ObservableCollection<TargetChipViewModel> Targets { get; } = new();
    public ObservableCollection<DayToggleViewModel> Days { get; }
    public IReadOnlyList<TargetPreset> Presets { get; }
    public IReadOnlyList<TargetPreset> WebPresets { get; } = TargetPresets.Web;
    public AppPickerViewModel AppPicker { get; }

    public ICommand AddWebsiteCommand { get; }
    public ICommand AddAppCommand { get; }
    public ICommand AddPresetCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (Set(ref _isOpen, value))
            {
                Raise(nameof(IsWeekly));
            }
        }
    }

    public bool IsNew
    {
        get => _isNew;
        private set
        {
            if (Set(ref _isNew, value))
            {
                Raise(nameof(CanDelete));
                Raise(nameof(EditorTitle));
            }
        }
    }

    public Guid Id
    {
        get => _id;
        private set => Set(ref _id, value);
    }

    public string EditorTitle => IsNew ? "Neue Regel" : "Regel bearbeiten";
    public bool CanDelete => !IsNew;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    public string TargetInput
    {
        get => _targetInput;
        set => Set(ref _targetInput, value);
    }

    public TargetPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => Set(ref _selectedPreset, value);
    }

    public string LimitText
    {
        get => _limitText;
        set
        {
            if (Set(ref _limitText, value))
            {
                UpdateLimitHint();
            }
        }
    }

    public string LimitHint
    {
        get => _limitHint;
        private set => Set(ref _limitHint, value);
    }

    public string GraceText
    {
        get => _graceText;
        set => Set(ref _graceText, value);
    }

    public string MaxGraceText
    {
        get => _maxGraceText;
        set => Set(ref _maxGraceText, value);
    }

    public string StartText
    {
        get => _startText;
        set => Set(ref _startText, value);
    }

    public string EndText
    {
        get => _endText;
        set => Set(ref _endText, value);
    }

    public bool IsAlways
    {
        get => _isAlways;
        set
        {
            if (Set(ref _isAlways, value))
            {
                Raise(nameof(IsWeekly));
            }
        }
    }

    public bool IsWeekly => !IsAlways;

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (Set(ref _errorText, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public void Open(BlockRule rule, bool isNew)
    {
        Id = rule.Id;
        _createdAt = rule.CreatedAt == default ? DateTimeOffset.Now : rule.CreatedAt;
        IsNew = isNew;
        Name = rule.Name;
        Note = rule.Note ?? string.Empty;
        TargetInput = string.Empty;
        LimitText = rule.DailyLimitMinutes?.ToString(German) ?? string.Empty;
        GraceText = rule.GraceMinutes.ToString(German);
        MaxGraceText = rule.MaxGraceUsesPerDay.ToString(German);
        Enabled = rule.Enabled;
        IsAlways = rule.Schedule.Mode == ScheduleMode.Always;

        var firstWindow = rule.Schedule.Windows.FirstOrDefault();
        StartText = firstWindow?.Start.ToString("HH:mm") ?? "09:00";
        EndText = firstWindow?.End.ToString("HH:mm") ?? "17:00";

        foreach (var day in Days)
        {
            day.IsSelected = rule.Schedule.Mode == ScheduleMode.Weekly
                ? rule.Schedule.Windows.Any(w => w.Day == day.Day)
                : day.Day is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
        }

        Targets.Clear();
        foreach (var target in rule.DistinctTargets())
        {
            AddChip(target);
        }

        SelectedPreset = null;
        AppPicker.Close();
        ErrorText = null;
        IsOpen = true;
        Raise(nameof(EditorTitle));
        Raise(nameof(CanDelete));
    }

    public void Close()
    {
        IsOpen = false;
        AppPicker.Close();
        ErrorText = null;
        _owner.Refresh();
    }

    public void SetError(string? message) => ErrorText = message;

    private void AddTarget(TargetKind kind)
    {
        var normalized = Target.Normalize(kind, TargetInput);
        if (normalized.Length == 0)
        {
            SetError(kind == TargetKind.Website
                ? "Bitte gib eine Website ein, z. B. youtube.com."
                : "Bitte gib ein Programm ein, z. B. chrome oder discord.");
            return;
        }

        if (Targets.Any(t => t.Target.Kind == kind
                             && t.Target.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            SetError("Dieses Ziel ist bereits in der Regel.");
            return;
        }

        AddChip(new Target(kind, normalized));
        TargetInput = string.Empty;
        ErrorText = null;
    }

    private void AddPreset()
    {
        if (SelectedPreset is null)
        {
            SetError("Wähle zuerst eine Vorlage.");
            return;
        }

        foreach (var target in SelectedPreset.Targets)
        {
            if (Targets.Any(t => t.Target.Kind == target.Kind
                                 && t.Target.Value.Equals(target.Value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddChip(target.Clone());
        }

        SelectedPreset = null;
        ErrorText = null;
    }

    private void AddInstalledApp(InstalledApplication app)
    {
        if (Targets.Any(t => t.Target.Kind == TargetKind.Process
                             && t.Target.Value.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            SetError($"{app.Name} ist bereits in der Regel.");
            return;
        }

        AddChip(new Target(TargetKind.Process, app.ProcessName, app.Name));
        ErrorText = null;
        AppPicker.SetStatus($"{app.Name} hinzugefügt.");
    }

    private void AddChip(Target target) =>
        Targets.Add(new TargetChipViewModel(target, chip => Targets.Remove(chip)));

    private void UpdateLimitHint() =>
        LimitHint = string.IsNullOrWhiteSpace(LimitText)
            ? "Ohne Limit gilt die Sperre während des gesamten Zeitplans."
            : "Freigabe pro Tag. Aufgaben und Nachfristen erhöhen das Budget.";

    private async Task SaveAsync()
    {
        ErrorText = null;
        var rule = Build();
        if (rule is null)
        {
            return;
        }

        await _owner.SaveRuleAsync(rule);
    }

    private async Task DeleteAsync()
    {
        if (!IsNew)
        {
            await _owner.DeleteRuleAsync(Id, Name);
        }
    }

    private BlockRule? Build()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Bitte gib der Regel einen Namen.");
        }

        if (Targets.Count == 0)
        {
            errors.Add("Füge mindestens ein Programm oder eine Website hinzu.");
        }

        Schedule schedule;
        if (IsAlways)
        {
            schedule = Schedule.Always();
        }
        else
        {
            var days = Days.Where(d => d.IsSelected).Select(d => d.Day).ToList();
            if (days.Count == 0)
            {
                errors.Add("Wähle mindestens einen Wochentag.");
            }

            if (!TryParseTime(StartText, out var start))
            {
                errors.Add("Die Startzeit ist ungültig (z. B. 09:00).");
                start = new TimeOnly(9, 0);
            }

            if (!TryParseTime(EndText, out var end))
            {
                errors.Add("Die Endzeit ist ungültig (z. B. 17:00).");
                end = new TimeOnly(17, 0);
            }

            schedule = new Schedule
            {
                Mode = ScheduleMode.Weekly,
                Windows = days.Select(day => new TimeWindow(day, start, end)).ToList()
            };
        }

        int? limit = null;
        if (!string.IsNullOrWhiteSpace(LimitText))
        {
            if (!int.TryParse(LimitText.Trim(), NumberStyles.Integer, German, out var parsed)
                || parsed is < 1 or > 1440)
            {
                errors.Add("Das Tageslimit muss zwischen 1 und 1440 Minuten liegen.");
            }
            else
            {
                limit = parsed;
            }
        }

        if (!int.TryParse(GraceText.Trim(), NumberStyles.Integer, German, out var grace) || grace is < 1 or > 60)
        {
            errors.Add("Die Nachfrist muss zwischen 1 und 60 Minuten liegen.");
            grace = 5;
        }

        if (!int.TryParse(MaxGraceText.Trim(), NumberStyles.Integer, German, out var maxGrace) || maxGrace is < 0 or > 10)
        {
            errors.Add("Nachfristen pro Tag: 0 bis 10.");
            maxGrace = 2;
        }

        var rule = new BlockRule
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
            Name = Name.Trim(),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            Enabled = Enabled,
            Targets = Targets.Select(t => t.Target.Clone()).ToList(),
            Schedule = schedule,
            DailyLimitMinutes = limit,
            GraceMinutes = grace,
            MaxGraceUsesPerDay = maxGrace,
            CreatedAt = _createdAt
        };

        errors.AddRange(RuleValidator.Validate(rule).Errors);

        if (errors.Count > 0)
        {
            SetError(string.Join(" ", errors.Distinct()));
            return null;
        }

        return rule;
    }

    private static bool TryParseTime(string text, out TimeOnly value) =>
        TimeOnly.TryParse(text.Trim(), German, DateTimeStyles.None, out value)
        || TimeOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}

public sealed class AllowanceEditorViewModel : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly RulesViewModel _owner;
    private readonly MainViewModel _main;

    private bool _isOpen;
    private bool _isNew;
    private Guid _id;
    private DateTimeOffset _createdAt = DateTimeOffset.Now;
    private string _name = string.Empty;
    private string _targetInput = string.Empty;
    private TargetPreset? _selectedPreset;
    private string _expiresText = string.Empty;
    private bool _enabled = true;
    private string? _errorText;

    public AllowanceEditorViewModel(RulesViewModel owner, MainViewModel main)
    {
        _owner = owner;
        _main = main;
        Presets = TargetPresets.All;

        AppPicker = new AppPickerViewModel(AddInstalledApp);
        AddWebsiteCommand = new RelayCommand(() => AddTarget(TargetKind.Website));
        AddAppCommand = new RelayCommand(() => AddTarget(TargetKind.Process));
        AddPresetCommand = new RelayCommand(AddPreset);
        SaveCommand = new AsyncRelayCommand(SaveAsync, ex => SetError(ex.Message));
        CancelCommand = new RelayCommand(Close);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, ex => SetError(ex.Message));
    }

    public ObservableCollection<TargetChipViewModel> Targets { get; } = new();
    public IReadOnlyList<TargetPreset> Presets { get; }
    public IReadOnlyList<TargetPreset> WebPresets { get; } = TargetPresets.Web;
    public AppPickerViewModel AppPicker { get; }

    public ICommand AddWebsiteCommand { get; }
    public ICommand AddAppCommand { get; }
    public ICommand AddPresetCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

    public bool IsNew
    {
        get => _isNew;
        private set
        {
            if (Set(ref _isNew, value))
            {
                Raise(nameof(CanDelete));
                Raise(nameof(EditorTitle));
            }
        }
    }

    public Guid Id
    {
        get => _id;
        private set => Set(ref _id, value);
    }

    public string EditorTitle => IsNew ? "Neue Ausnahme" : "Ausnahme bearbeiten";
    public bool CanDelete => !IsNew;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string TargetInput
    {
        get => _targetInput;
        set => Set(ref _targetInput, value);
    }

    public TargetPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => Set(ref _selectedPreset, value);
    }

    public string ExpiresText
    {
        get => _expiresText;
        set => Set(ref _expiresText, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (Set(ref _errorText, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public void Open(Allowance allowance, bool isNew)
    {
        Id = allowance.Id;
        _createdAt = allowance.CreatedAt == default ? DateTimeOffset.Now : allowance.CreatedAt;
        IsNew = isNew;
        Name = allowance.Name;
        TargetInput = string.Empty;
        ExpiresText = allowance.ExpiresAt?.LocalDateTime.ToString("dd.MM.yyyy", German) ?? string.Empty;
        Enabled = allowance.Enabled;
        Targets.Clear();
        foreach (var target in allowance.Targets)
        {
            AddChip(target.Clone());
        }

        SelectedPreset = null;
        AppPicker.Close();
        ErrorText = null;
        IsOpen = true;
        Raise(nameof(EditorTitle));
        Raise(nameof(CanDelete));
    }

    public void Close()
    {
        IsOpen = false;
        AppPicker.Close();
        ErrorText = null;
        _owner.Refresh();
    }

    public void SetError(string? message) => ErrorText = message;

    private void AddTarget(TargetKind kind)
    {
        var normalized = Target.Normalize(kind, TargetInput);
        if (normalized.Length == 0)
        {
            SetError(kind == TargetKind.Website
                ? "Bitte gib eine Website ein, z. B. youtube.com."
                : "Bitte gib ein Programm ein, z. B. chrome.");
            return;
        }

        if (Targets.Any(t => t.Target.Kind == kind
                             && t.Target.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            SetError("Dieses Ziel ist bereits enthalten.");
            return;
        }

        AddChip(new Target(kind, normalized));
        TargetInput = string.Empty;
        ErrorText = null;
    }

    private void AddPreset()
    {
        if (SelectedPreset is null)
        {
            SetError("Wähle zuerst eine Vorlage.");
            return;
        }

        foreach (var target in SelectedPreset.Targets)
        {
            if (Targets.Any(t => t.Target.Kind == target.Kind
                                 && t.Target.Value.Equals(target.Value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddChip(target.Clone());
        }

        SelectedPreset = null;
        ErrorText = null;
    }

    private void AddInstalledApp(InstalledApplication app)
    {
        if (Targets.Any(t => t.Target.Kind == TargetKind.Process
                             && t.Target.Value.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            SetError($"{app.Name} ist bereits enthalten.");
            return;
        }

        AddChip(new Target(TargetKind.Process, app.ProcessName, app.Name));
        ErrorText = null;
        AppPicker.SetStatus($"{app.Name} hinzugefügt.");
    }

    private void AddChip(Target target) =>
        Targets.Add(new TargetChipViewModel(target, chip => Targets.Remove(chip)));

    private async Task SaveAsync()
    {
        ErrorText = null;
        var allowance = Build();
        if (allowance is null)
        {
            return;
        }

        await _owner.SaveAllowanceAsync(allowance);
    }

    private async Task DeleteAsync()
    {
        if (!IsNew)
        {
            await _owner.DeleteAllowanceAsync(Id, Name);
        }
    }

    private Allowance? Build()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Bitte gib der Ausnahme einen Namen.");
        }

        if (Targets.Count == 0)
        {
            errors.Add("Füge mindestens ein Ziel hinzu, das erlaubt sein soll.");
        }

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(ExpiresText))
        {
            if (!TryParseDate(ExpiresText, out var date))
            {
                errors.Add("Das Ablaufdatum ist ungültig (z. B. 30.11.2026).");
            }
            else
            {
                var local = date.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified);
                expiresAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                if (expiresAt <= DateTimeOffset.Now)
                {
                    errors.Add("Das Ablaufdatum liegt in der Vergangenheit.");
                }
            }
        }

        var allowance = new Allowance
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
            Name = Name.Trim(),
            Enabled = Enabled,
            Targets = Targets.Select(t => t.Target.Clone()).ToList(),
            Schedule = Schedule.Always(),
            ExpiresAt = expiresAt,
            CreatedAt = _createdAt
        };

        errors.AddRange(RuleValidator.Validate(allowance).Errors);

        if (errors.Count > 0)
        {
            SetError(string.Join(" ", errors.Distinct()));
            return null;
        }

        return allowance;
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParse(text.Trim(), German, DateTimeStyles.None, out date)
        || DateOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
