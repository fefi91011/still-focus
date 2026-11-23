using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Still.Core.Models;

namespace Still.App.ViewModels;

/// <summary>
/// The redesigned rule editor. Sections run top to bottom: name, schedule,
/// grace, targets, websited scope and a live summary of the resulting rule.
/// </summary>
public sealed class RuleEditorViewModel : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly RulesViewModel _owner;

    private bool _isOpen;
    private bool _isNew;
    private Guid _id;
    private DateTimeOffset _createdAt = DateTimeOffset.Now;
    private string _name = string.Empty;
    private string? _note;
    private string _limitText = "60";
    private string _graceText = "5";
    private string _maxGraceText = "2";
    private string _startText = "09:00";
    private string _endText = "17:00";
    private string _durationText = "90";
    private bool _scheduleEnabled;
    private bool _useDuration;
    private bool _enabled = true;
    private string? _errorText;
    private string _summary = string.Empty;
    private string _summaryDetail = string.Empty;

    public RuleEditorViewModel(RulesViewModel owner)
    {
        _owner = owner;

        Days = new ObservableCollection<DayToggleViewModel>(
            Enum.GetValues<DayOfWeek>()
                .Select(d => new DayToggleViewModel(d, d is >= DayOfWeek.Monday and <= DayOfWeek.Friday))
                .ToList());

        foreach (var day in Days)
        {
            day.PropertyChanged += (_, _) => UpdateSummary();
        }

        Selection = new TargetSelectionViewModel(OnSelectionChanged, SetError);

        CancelCommand = new RelayCommand(Close);
        SaveCommand = new AsyncRelayCommand(SaveAsync, ex => SetError(ex.Message));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, ex => SetError(ex.Message));
        UseAlwaysCommand = new RelayCommand(() => ScheduleEnabled = false);
        UseWindowCommand = new RelayCommand(() => ScheduleEnabled = true);
        UseEndTimeCommand = new RelayCommand(() => UseDuration = false);
        UseDurationCommand = new RelayCommand(() => UseDuration = true);
    }

    public ObservableCollection<DayToggleViewModel> Days { get; }
    public TargetSelectionViewModel Selection { get; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UseAlwaysCommand { get; }
    public ICommand UseWindowCommand { get; }
    public ICommand UseEndTimeCommand { get; }
    public ICommand UseDurationCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (Set(ref _isOpen, value))
            {
                Raise(nameof(IsEditing));
            }
        }
    }

    public bool IsEditing => IsOpen;

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
    public string EditorLead => IsNew
        ? "Was soll wann gesperrt sein? Alles lässt sich später ändern."
        : "Passe an, was diese Regel greift.";
    public bool CanDelete => !IsNew;

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                UpdateSummary();
            }
        }
    }

    public string LimitText
    {
        get => _limitText;
        set
        {
            if (Set(ref _limitText, value))
            {
                Raise(nameof(LimitHint));
                UpdateSummary();
            }
        }
    }

    public string LimitHint => string.IsNullOrWhiteSpace(LimitText)
        ? "Ohne Limit gilt die Sperre während des gesamten Zeitplans."
        : "Freigabe pro Tag. Aufgaben und Nachfristen erhöhen das Budget.";

    public string GraceText
    {
        get => _graceText;
        set
        {
            if (Set(ref _graceText, value))
            {
                UpdateSummary();
            }
        }
    }

    public string MaxGraceText
    {
        get => _maxGraceText;
        set
        {
            if (Set(ref _maxGraceText, value))
            {
                UpdateSummary();
            }
        }
    }

    public string StartText
    {
        get => _startText;
        set
        {
            if (Set(ref _startText, value))
            {
                UpdateSummary();
            }
        }
    }

    public string EndText
    {
        get => _endText;
        set
        {
            if (Set(ref _endText, value))
            {
                UpdateSummary();
            }
        }
    }

    public string DurationText
    {
        get => _durationText;
        set
        {
            if (Set(ref _durationText, value))
            {
                UpdateSummary();
            }
        }
    }

    /// <summary>On: the rule only blocks inside the time windows. Off: always.</summary>
    public bool ScheduleEnabled
    {
        get => _scheduleEnabled;
        set
        {
            if (Set(ref _scheduleEnabled, value))
            {
                Raise(nameof(IsAlwaysMode));
                Raise(nameof(IsWindowMode));
                UpdateSummary();
            }
        }
    }

    public bool IsAlwaysMode => !ScheduleEnabled;
    public bool IsWindowMode => ScheduleEnabled;

    public bool UseDuration
    {
        get => _useDuration;
        set
        {
            if (Set(ref _useDuration, value))
            {
                Raise(nameof(UseEndTime));
                UpdateSummary();
            }
        }
    }

    public bool UseEndTime => !UseDuration;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
            {
                UpdateSummary();
            }
        }
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

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public string SummaryDetail
    {
        get => _summaryDetail;
        private set => Set(ref _summaryDetail, value);
    }

    public void Open(BlockRule rule, bool isNew)
    {
        Id = rule.Id;
        _createdAt = rule.CreatedAt == default ? DateTimeOffset.Now : rule.CreatedAt;
        _note = rule.Note;
        IsNew = isNew;
        Name = rule.Name;
        LimitText = rule.DailyLimitMinutes?.ToString(German) ?? string.Empty;
        GraceText = rule.GraceMinutes.ToString(German);
        MaxGraceText = rule.MaxGraceUsesPerDay.ToString(German);
        Enabled = rule.Enabled;
        ScheduleEnabled = rule.Schedule.Mode == ScheduleMode.Weekly;
        UseDuration = false;
        DurationText = "90";

        var firstWindow = rule.Schedule.Windows.FirstOrDefault();
        StartText = firstWindow?.Start.ToString("HH:mm") ?? "09:00";
        EndText = firstWindow?.End.ToString("HH:mm") ?? "17:00";

        foreach (var day in Days)
        {
            day.IsSelected = rule.Schedule.Mode == ScheduleMode.Weekly
                ? rule.Schedule.Windows.Any(w => w.Day == day.Day)
                : day.Day is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
        }

        ErrorText = null;
        Selection.Load(rule.DistinctTargets());
        IsOpen = true;
        Raise(nameof(EditorTitle));
        Raise(nameof(EditorLead));
        Raise(nameof(CanDelete));
        UpdateSummary();
    }

    public void Close()
    {
        IsOpen = false;
        ErrorText = null;
        _owner.Refresh();
    }

    public void SetError(string? message) => ErrorText = message;

    private void OnSelectionChanged()
    {
        ErrorText = null;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var apps = Selection.AppCount;
        var sites = Selection.WebsiteCount;
        Summary = apps + sites == 0
            ? "Noch keine Ziele gewählt"
            : string.Join(" · ", new[]
            {
                apps > 0 ? (apps == 1 ? "1 Programm" : $"{apps} Programme") : null,
                sites > 0 ? (sites == 1 ? "1 Webseite" : $"{sites} Webseiten") : null
            }.Where(part => part is not null));

        var parts = new List<string>();
        if (!Enabled)
        {
            parts.Add("Regel pausiert");
        }

        parts.Add(DescribeSchedule());

        var limit = LimitText.Trim();
        parts.Add(limit.Length == 0 ? "ohne Tageslimit" : $"{limit} Min pro Tag");

        if (int.TryParse(MaxGraceText.Trim(), NumberStyles.Integer, German, out var uses) && uses > 0)
        {
            parts.Add($"{uses}× {GraceText.Trim()} Min Nachfrist");
        }
        else
        {
            parts.Add("keine Nachfrist");
        }

        var singleDomain = Selection.Targets.Count(t => t.IsWebsite && !t.Target.IncludeSubdomains);
        if (singleDomain > 0)
        {
            parts.Add(singleDomain == 1 ? "1 Domain ohne Subdomains" : $"{singleDomain} Domains ohne Subdomains");
        }

        SummaryDetail = string.Join("   ·   ", parts);
    }

    private string DescribeSchedule()
    {
        if (!ScheduleEnabled)
        {
            return "immer aktiv";
        }

        var days = Days.Where(d => d.IsSelected).Select(d => d.Label).ToList();
        var dayText = days.Count switch
        {
            0 => "kein Tag",
            7 => "täglich",
            _ => string.Join(", ", days)
        };

        if (UseDuration)
        {
            return $"{dayText} · ab {StartText.Trim()} für {DurationText.Trim()} Min";
        }

        return $"{dayText} · {StartText.Trim()}–{EndText.Trim()}";
    }

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

        if (Selection.Targets.Count == 0)
        {
            errors.Add("Füge mindestens ein Programm oder eine Website hinzu.");
        }

        Schedule schedule;
        if (!ScheduleEnabled)
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

            TimeOnly end;
            if (UseDuration)
            {
                if (!int.TryParse(DurationText.Trim(), NumberStyles.Integer, German, out var minutes)
                    || minutes is < 5 or > 1440)
                {
                    errors.Add("Die Dauer muss zwischen 5 und 1440 Minuten liegen.");
                    minutes = 90;
                }

                end = start.Add(TimeSpan.FromMinutes(minutes));
            }
            else if (!TryParseTime(EndText, out end))
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
            Note = _note,
            Enabled = Enabled,
            Targets = Selection.Targets.Select(t => t.Target.Clone()).ToList(),
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
