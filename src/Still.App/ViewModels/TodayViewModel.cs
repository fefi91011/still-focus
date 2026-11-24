using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using Still.App.Controls;
using Still.Core.Engine;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class TodayViewModel : ObservableObject, IPageViewModel
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly MainViewModel _main;
    private string _signature = "init";
    private string _greeting = string.Empty;
    private string _heroStatus = string.Empty;
    private string _heroSubline = string.Empty;
    private string _dateLine = string.Empty;
    private string _focusToday = "0";
    private string _blocksToday = "0";
    private string _tasksToday = "0";
    private string _screenTimeToday = "0 Min";
    private string _screenTimeAverage = "0 Min";
    private string _screenTimeWeek = "0 Min";
    private IReadOnlyList<ChartBar> _screenTimeBars = Array.Empty<ChartBar>();
    private AppUsageSectionViewModel _appsToday = new();
    private AppUsageSectionViewModel _appsWeek = new();
    private string _appUsageSignature = "init";
    private WebsiteUsageSectionViewModel _websitesToday = new();
    private WebsiteUsageSectionViewModel _websitesWeek = new();
    private string _websiteUsageSignature = "init";
    private string _usedToday = "0 Min";
    private string _activeRulesText = string.Empty;
    private string _weekSummary = string.Empty;
    private string _nextBlockText = string.Empty;
    private bool _noRules;
    private bool _hasTasks;
    private bool _hasExceptions;
    private bool _lockdownActive;
    private bool _hasWebsiteTargets;
    private bool _hasAppTargets;
    private string _targetsSummary = string.Empty;

    public TodayViewModel(MainViewModel main)
    {
        _main = main;
        OpenRulesCommand = new RelayCommand(() => main.Navigate("rules"));
        OpenFocusCommand = new RelayCommand(() => main.Navigate("focus"));
        OpenStatsCommand = new RelayCommand(() => main.Navigate("stats"));
        OpenTasksCommand = new RelayCommand(() => main.Navigate("tasks"));
        Refresh();
    }

    public ObservableCollection<RuleCardViewModel> RuleCards { get; } = new();
    public ObservableCollection<TaskMiniViewModel> Tasks { get; } = new();
    public ObservableCollection<ExceptionRowViewModel> Exceptions { get; } = new();
    public ObservableCollection<OverviewTargetViewModel> WebsiteTargets { get; } = new();
    public ObservableCollection<OverviewTargetViewModel> AppTargets { get; } = new();
    public IReadOnlyList<ChartBar> WeekBars { get; private set; } = Array.Empty<ChartBar>();

    public ICommand OpenRulesCommand { get; }
    public ICommand OpenFocusCommand { get; }
    public ICommand OpenStatsCommand { get; }
    public ICommand OpenTasksCommand { get; }

    public string Greeting
    {
        get => _greeting;
        private set => Set(ref _greeting, value);
    }

    public string HeroStatus
    {
        get => _heroStatus;
        private set => Set(ref _heroStatus, value);
    }

    public string HeroSubline
    {
        get => _heroSubline;
        private set => Set(ref _heroSubline, value);
    }

    public string DateLine
    {
        get => _dateLine;
        private set => Set(ref _dateLine, value);
    }

    public string FocusToday
    {
        get => _focusToday;
        private set => Set(ref _focusToday, value);
    }

    public string BlocksToday
    {
        get => _blocksToday;
        private set => Set(ref _blocksToday, value);
    }

    public string TasksToday
    {
        get => _tasksToday;
        private set => Set(ref _tasksToday, value);
    }

    public string ScreenTimeToday
    {
        get => _screenTimeToday;
        private set => Set(ref _screenTimeToday, value);
    }

    public string ScreenTimeAverage
    {
        get => _screenTimeAverage;
        private set => Set(ref _screenTimeAverage, value);
    }

    public string ScreenTimeWeek
    {
        get => _screenTimeWeek;
        private set => Set(ref _screenTimeWeek, value);
    }

    public IReadOnlyList<ChartBar> ScreenTimeBars
    {
        get => _screenTimeBars;
        private set => Set(ref _screenTimeBars, value);
    }

    public AppUsageSectionViewModel AppsToday
    {
        get => _appsToday;
        private set => Set(ref _appsToday, value);
    }

    public AppUsageSectionViewModel AppsWeek
    {
        get => _appsWeek;
        private set => Set(ref _appsWeek, value);
    }

    public WebsiteUsageSectionViewModel WebsitesToday
    {
        get => _websitesToday;
        private set => Set(ref _websitesToday, value);
    }

    public WebsiteUsageSectionViewModel WebsitesWeek
    {
        get => _websitesWeek;
        private set => Set(ref _websitesWeek, value);
    }

    public string UsedToday
    {
        get => _usedToday;
        private set => Set(ref _usedToday, value);
    }

    public string ActiveRulesText
    {
        get => _activeRulesText;
        private set => Set(ref _activeRulesText, value);
    }

    public string WeekSummary
    {
        get => _weekSummary;
        private set => Set(ref _weekSummary, value);
    }

    public string NextBlockText
    {
        get => _nextBlockText;
        private set => Set(ref _nextBlockText, value);
    }

    public bool NoRules
    {
        get => _noRules;
        private set => Set(ref _noRules, value);
    }

    public bool HasTasks
    {
        get => _hasTasks;
        private set => Set(ref _hasTasks, value);
    }

    public bool HasExceptions
    {
        get => _hasExceptions;
        private set => Set(ref _hasExceptions, value);
    }

    public bool HasWebsiteTargets
    {
        get => _hasWebsiteTargets;
        private set
        {
            if (Set(ref _hasWebsiteTargets, value))
            {
                Raise(nameof(HasTargets));
            }
        }
    }

    public bool HasAppTargets
    {
        get => _hasAppTargets;
        private set
        {
            if (Set(ref _hasAppTargets, value))
            {
                Raise(nameof(HasTargets));
            }
        }
    }

    public bool HasTargets => HasWebsiteTargets || HasAppTargets;

    public string TargetsSummary
    {
        get => _targetsSummary;
        private set => Set(ref _targetsSummary, value);
    }

    public bool LockdownActive
    {
        get => _lockdownActive;
        private set => Set(ref _lockdownActive, value);
    }

    public void Refresh()
    {
        var state = _main.Runtime.State;
        var snapshot = _main.Runtime.Snapshot;
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var day = state.Days.FirstOrDefault(d => d.Date == today);
        var enabledRules = state.Rules.Where(r => r.Enabled).ToList();

        NoRules = enabledRules.Count == 0;
        LockdownActive = snapshot.LockdownActive;
        Greeting = now.Hour switch
        {
            < 5 => "Noch wach?",
            < 11 => "Guten Morgen",
            < 17 => "Guten Tag",
            < 22 => "Guten Abend",
            _ => "Gute Nacht"
        };
        DateLine = now.ToString("dddd, d. MMMM yyyy", German);
        FocusToday = (day?.FocusMinutes ?? 0).ToString("0", German);
        BlocksToday = (day?.BlockedAttempts ?? 0).ToString("0", German);
        TasksToday = (day?.TasksCompleted ?? 0).ToString("0", German);
        ScreenTimeToday = FormatDuration(day?.ScreenSeconds ?? 0);
        var screenWeekSeconds = Enumerable.Range(0, 7)
            .Select(offset => state.Days.FirstOrDefault(d => d.Date == today.AddDays(-offset))?.ScreenSeconds ?? 0)
            .Sum();
        ScreenTimeWeek = FormatDuration(screenWeekSeconds);
        ScreenTimeAverage = FormatDuration((int)Math.Round(screenWeekSeconds / 7.0));
        UpdateAppUsage(state, today);
        UpdateWebsiteUsage(today);
        UsedToday = FormatDuration(snapshot.Rules
            .Where(r => r.Enabled && r.DailyLimitMinutes is not null)
            .Sum(r => r.UsedSeconds));
        ActiveRulesText = enabledRules.Count == 0
            ? "Noch keine Regeln"
            : $"{enabledRules.Count} Regeln · {snapshot.Rules.Count(r => r.Enabled && r.Armed)} aktiv";

        var blockedNames = snapshot.Rules.Where(r => r.Blocked).Select(r => r.Name).Distinct().ToList();
        if (snapshot.LockdownActive && snapshot.Lockdown is { } lockdown)
        {
            HeroStatus = "Stillstand.";
            HeroSubline = $"Cold Turkey läuft bis {lockdown.EndsAt.LocalDateTime:HH:mm} Uhr. Keine Abkürzung, keine Ausrede.";
        }
        else if (snapshot.Pomodoro is { IsRunning: true } pomodoro)
        {
            HeroStatus = pomodoro.Phase == PomodoroPhase.Work ? "Fokus." : "Pause.";
            HeroSubline = pomodoro.Phase == PomodoroPhase.Work
                ? "Alle Regel-Ziele sind gesperrt. Deine Pause kommt automatisch."
                : "Kurz durchatmen. Danach geht es weiter.";
        }
        else if (blockedNames.Count > 0)
        {
            HeroStatus = "Grenzen stehen.";
            HeroSubline = $"{string.Join(" und ", blockedNames.Take(2))} sind gerade gesperrt. Der Rest des Tages gehört dir.";
        }
        else if (enabledRules.Count == 0)
        {
            HeroStatus = "Alles frei.";
            HeroSubline = "Noch ist nichts eingestellt. Eine Regel reicht, um anzufangen.";
        }
        else
        {
            HeroStatus = "Alles frei.";
            HeroSubline = "Deine Regeln sind vorbereitet. Im Moment darfst du durchatmen.";
        }

        var sig = BuildSignature(state, snapshot, today);
        if (sig != _signature)
        {
            _signature = sig;
            RebuildCards(state, snapshot, enabledRules, now);
            RebuildTargets(state, snapshot, enabledRules, now);
            RebuildTasks(state, today);
            RebuildExceptions(state, now);
            WeekBars = BuildWeekBars(state, today);
            ScreenTimeBars = BuildScreenTimeBars(state, today);
            var weekTotal = state.Days
                .Where(d => d.Date > today.AddDays(-7) && d.Date <= today)
                .Sum(d => d.FocusMinutes);
            WeekSummary = $"{weekTotal} Min in 7 Tagen · Ø {Math.Round(weekTotal / 7.0)} Min pro Tag";
        }

        NextBlockText = BuildNextText(snapshot, now);
    }

    private void RebuildCards(
        AppState state,
        Core.Engine.EngineSnapshot snapshot,
        List<BlockRule> rules,
        DateTimeOffset now)
    {
        RuleCards.Clear();

        foreach (var rule in rules)
        {
            var status = snapshot.FindRule(rule.Id);
            if (status is null)
            {
                continue;
            }

            var limit = rule.DailyLimitMinutes is int value && value > 0 ? value : (int?)null;
            var blocked = status.Blocked || snapshot.LockdownActive;
            var warningThreshold = state.Settings.WarningThresholdPercent / 100.0;
            var isWarning = limit is not null && !blocked && status.Fraction >= warningThreshold;

            var statusText = snapshot.LockdownActive
                ? "Cold Turkey"
                : status.Blocked
                    ? "Gesperrt"
                    : !status.Armed
                        ? "Später"
                        : limit is null
                            ? "Frei"
                            : $"{status.RemainingMinutes} Min übrig";

            var budgetText = limit is null
                ? status.Armed ? "Keine Freigabe" : "Außerhalb des Zeitplans"
                : $"{status.UsedMinutes} / {limit + status.BonusMinutes + status.GraceBonusMinutes} Min";

            var targets = rule.DistinctTargets()
                .Select(t => t.Label ?? t.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var targetsText = string.Join(" · ", targets.Take(3));
            if (targets.Count > 3)
            {
                targetsText += $" +{targets.Count - 3}";
            }

            var graceCommand = new RelayCommand(() => _main.GrantGrace(rule.Id));

            RuleCards.Add(new RuleCardViewModel
            {
                RuleId = rule.Id,
                Name = rule.Name,
                TargetsText = targetsText,
                StatusText = statusText,
                BudgetText = budgetText,
                ScheduleText = rule.Schedule.Describe(),
                Fraction = limit is null ? (blocked ? 1 : 0) : status.Fraction,
                IsBlocked = blocked,
                IsWarning = isWarning,
                CanGrace = !snapshot.LockdownActive && status.CanGrantGrace,
                GraceLabel = $"+{rule.GraceMinutes} Min",
                GraceCommand = graceCommand,
                BarBrush = blocked ? UiBrushes.Danger : isWarning ? UiBrushes.Warning : UiBrushes.Accent
            });
        }
    }

    private void RebuildTargets(
        AppState state,
        EngineSnapshot snapshot,
        IReadOnlyList<BlockRule> rules,
        DateTimeOffset now)
    {
        WebsiteTargets.Clear();
        AppTargets.Clear();

        var entries = new Dictionary<string, (Target Target, List<RuleStatus> Statuses)>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var status = snapshot.FindRule(rule.Id);
            if (status is null)
            {
                continue;
            }

            foreach (var target in rule.DistinctTargets())
            {
                var key = $"{(int)target.Kind}:{target.Value}";
                if (!entries.TryGetValue(key, out var entry))
                {
                    entry = (target.Clone(), new List<RuleStatus>());
                    entries[key] = entry;
                }

                entry.Statuses.Add(status);
            }
        }

        var rows = entries.Values
            .Select(entry =>
            {
                var isExempt = state.Allowances.Any(allowance =>
                    allowance.IsActiveAt(now) && allowance.Covers(entry.Target));
                var isBlocked = !isExempt && (snapshot.LockdownActive || entry.Statuses.Any(status => status.Blocked));
                var isArmed = entry.Statuses.Any(status => status.Armed);
                var statusText = isExempt
                    ? "Ausnahme"
                    : isBlocked
                        ? "Gesperrt"
                        : isArmed
                            ? "Bereit"
                            : "Später";
                var brush = isExempt
                    ? UiBrushes.Info
                    : isBlocked
                        ? UiBrushes.Danger
                        : isArmed
                            ? UiBrushes.Accent
                            : UiBrushes.Dim;

                return new OverviewTargetViewModel(entry.Target, statusText, brush, isBlocked, isExempt);
            })
            .OrderByDescending(row => row.IsBlocked)
            .ThenBy(row => row.IsExempt ? 1 : 0)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var row in rows)
        {
            if (row.Kind == TargetKind.Website)
            {
                WebsiteTargets.Add(row);
            }
            else
            {
                AppTargets.Add(row);
            }
        }

        HasWebsiteTargets = WebsiteTargets.Count > 0;
        HasAppTargets = AppTargets.Count > 0;

        var summary = new List<string>();
        if (WebsiteTargets.Count > 0)
        {
            summary.Add(WebsiteTargets.Count == 1 ? "1 Webseite" : $"{WebsiteTargets.Count} Webseiten");
        }

        if (AppTargets.Count > 0)
        {
            summary.Add(AppTargets.Count == 1 ? "1 App" : $"{AppTargets.Count} Apps");
        }

        TargetsSummary = string.Join(" · ", summary);
    }

    private void RebuildTasks(AppState state, DateOnly today)
    {
        Tasks.Clear();
        foreach (var task in state.Tasks.Where(t => t.Enabled).Take(4))
        {
            var done = state.Completions.Count(c =>
                c.TaskId == task.Id && DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today);
            var complete = done < task.MaxPerDay;
            Tasks.Add(new TaskMiniViewModel
            {
                Title = task.Title,
                RewardText = $"+{task.RewardMinutes} Min",
                DetailText = task.RequireReflection ? "Mit kurzer Notiz" : $"Heute {done}/{task.MaxPerDay}",
                CanComplete = complete,
                CompleteCommand = new AsyncRelayCommand(
                    () => _main.CompleteTaskAsync(task.Id, task.Title, task.RequireReflection),
                    ex => _main.ShowToast("Aufgabe fehlgeschlagen: " + ex.Message, error: true))
            });
        }

        HasTasks = Tasks.Count > 0;
    }

    private void RebuildExceptions(AppState state, DateTimeOffset now)
    {
        Exceptions.Clear();
        foreach (var allowance in state.Allowances.Where(a => a.Enabled).OrderByDescending(a => a.IsActiveAt(now)))
        {
            Exceptions.Add(new ExceptionRowViewModel(
                allowance.Name,
                string.Join(" · ", allowance.Targets.Select(t => t.Label ?? t.Value).Distinct().Take(3)),
                allowance.IsActiveAt(now)
                    ? allowance.ExpiresAt is null ? "Jetzt aktiv" : $"Aktiv bis {allowance.ExpiresAt.Value.LocalDateTime:dd.MM.}"
                    : allowance.ExpiresAt is null ? "Gerade nicht aktiv" : $"Inaktiv · bis {allowance.ExpiresAt.Value.LocalDateTime:dd.MM.}",
                allowance.IsActiveAt(now)));
        }

        HasExceptions = Exceptions.Count > 0;
    }

    private void UpdateAppUsage(AppState state, DateOnly today)
    {
        var signature = string.Join("|", state.AppUsage
            .Where(entry => entry.Date >= today.AddDays(-6) && entry.Date <= today)
            .Select(entry => $"{entry.Date}:{entry.ProcessName}:{entry.UsedSeconds / 10}"));

        if (signature == _appUsageSignature)
        {
            return;
        }

        _appUsageSignature = signature;
        AppsToday = AppUsageSectionViewModel.Create(
            state.AppUsage,
            today,
            today,
            "Heute",
            5,
            "Sobald du eine App benutzt, erscheint sie hier.");
        AppsWeek = AppUsageSectionViewModel.Create(
            state.AppUsage,
            today.AddDays(-6),
            today,
            "Letzte 7 Tage",
            5,
            "Noch keine App-Zeit in den letzten 7 Tagen.");
    }

    private void UpdateWebsiteUsage(DateOnly today)
    {
        var entries = _main.Runtime.GetWebsiteUsageSnapshot();
        var signature = string.Join("|", entries
            .Where(entry => entry.Date >= today.AddDays(-6) && entry.Date <= today)
            .Select(entry => $"{entry.Date}:{entry.Host}:{entry.UsedSeconds / 10}"));

        if (signature == _websiteUsageSignature)
        {
            return;
        }

        _websiteUsageSignature = signature;
        WebsitesToday = WebsiteUsageSectionViewModel.Create(
            entries,
            today,
            today,
            "Heute",
            5,
            "Sobald die Erweiterung eine Website meldet, erscheint sie hier.");
        WebsitesWeek = WebsiteUsageSectionViewModel.Create(
            entries,
            today.AddDays(-6),
            today,
            "Letzte 7 Tage",
            5,
            "Noch keine Website-Zeit in den letzten 7 Tagen.");
    }

    private static IReadOnlyList<ChartBar> BuildScreenTimeBars(AppState state, DateOnly today)
    {
        var bars = new List<ChartBar>();
        for (var offset = -6; offset <= 0; offset++)
        {
            var date = today.AddDays(offset);
            var minutes = (int)Math.Round(
                (state.Days.FirstOrDefault(d => d.Date == date)?.ScreenSeconds ?? 0) / 60.0);
            bars.Add(new ChartBar
            {
                Label = offset == 0 ? "Heute" : Schedule.ShortDay(date.DayOfWeek),
                Value = minutes,
                ValueLabel = minutes <= 0 ? "–" : FormatBarDuration(minutes),
                Highlight = offset == 0
            });
        }

        return bars;
    }

    private static string FormatBarDuration(int minutes) =>
        minutes < 60 ? $"{minutes}m" : $"{minutes / 60}:{minutes % 60:00}h";

    private static IReadOnlyList<ChartBar> BuildWeekBars(AppState state, DateOnly today)
    {
        var bars = new List<ChartBar>();
        for (var offset = -6; offset <= 0; offset++)
        {
            var date = today.AddDays(offset);
            var minutes = state.Days.FirstOrDefault(d => d.Date == date)?.FocusMinutes ?? 0;
            bars.Add(new ChartBar
            {
                Label = offset == 0 ? "Heute" : Schedule.ShortDay(date.DayOfWeek),
                Value = minutes,
                ValueLabel = minutes > 0 ? minutes.ToString() : "–",
                Highlight = offset == 0
            });
        }

        return bars;
    }

    private static string FormatDuration(int seconds)
    {
        var minutes = (int)Math.Round(Math.Max(0, seconds) / 60.0);
        return minutes < 60
            ? $"{minutes} Min"
            : $"{minutes / 60}:{minutes % 60:00} h";
    }

    private static string BuildNextText(Core.Engine.EngineSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.LockdownActive && snapshot.Lockdown is { } lockdown)
        {
            return $"Cold Turkey bis {lockdown.EndsAt.LocalDateTime:HH:mm}";
        }

        if (snapshot.Pomodoro is { IsRunning: true } pomodoro)
        {
            return pomodoro.Phase == PomodoroPhase.Work
                ? $"Sperre bis {pomodoro.PhaseEndsAt.LocalDateTime:HH:mm}"
                : $"Nächste Fokusphase um {pomodoro.PhaseEndsAt.LocalDateTime:HH:mm}";
        }

        var next = snapshot.Rules
            .Where(r => r.Enabled && r.BlockStartsAt is not null && r.BlockStartsAt > now)
            .OrderBy(r => r.BlockStartsAt)
            .FirstOrDefault();

        return next is null
            ? "Keine weitere Sperre geplant"
            : $"Nächste Sperre {next.BlockStartsAt!.Value.LocalDateTime:HH:mm} · {next.Name}";
    }

    private static string BuildSignature(
        AppState state,
        Core.Engine.EngineSnapshot snapshot,
        DateOnly today)
    {
        var rules = string.Join("|", snapshot.Rules.Select(r =>
            $"{r.RuleId}:{r.Blocked}:{r.Armed}:{r.UsedMinutes}:{r.RemainingMinutes}:{r.GraceUses}:{r.BonusMinutes}"));
        var tasks = string.Join("|", state.Tasks.Select(t =>
            $"{t.Id}:{t.Title}:{t.RewardMinutes}:{t.MaxPerDay}:{t.Enabled}:{t.RequireReflection}"));
        var completions = string.Join("|", state.Completions
            .Where(c => DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today)
            .Select(c => c.Id));
        var allowances = string.Join("|", state.Allowances.Select(a =>
            $"{a.Id}:{a.Name}:{a.Enabled}:{a.IsActiveAt(DateTimeOffset.Now)}:{a.ExpiresAt}"));
        var days = string.Join("|", state.Days.Where(d => d.Date > today.AddDays(-7)).Select(d => $"{d.Date}:{d.FocusMinutes}:{d.ScreenSeconds / 60}"));
        var ruleDefinitions = string.Join("|", state.Rules.Select(rule =>
            $"{rule.Id}:{rule.Name}:{rule.Enabled}:{rule.DailyLimitMinutes}:{rule.GraceMinutes}:{rule.MaxGraceUsesPerDay}:{rule.Schedule.Describe()}:" +
            string.Join(",", rule.DistinctTargets().Select(target => $"{(int)target.Kind}:{target.Value}:{target.Label}"))));
        var mode = snapshot.LockdownActive ? "lock" : snapshot.Pomodoro?.Phase.ToString() ?? "idle";
        return $"{rules}§{ruleDefinitions}§{tasks}§{completions}§{allowances}§{days}§{mode}";
    }
}

public sealed class RuleCardViewModel
{
    public Guid RuleId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string BudgetText { get; init; } = string.Empty;
    public string ScheduleText { get; init; } = string.Empty;
    public double Fraction { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsWarning { get; init; }
    public bool CanGrace { get; init; }
    public string GraceLabel { get; init; } = string.Empty;
    public ICommand GraceCommand { get; init; } = new RelayCommand(() => { });
    public IBrush BarBrush { get; init; } = UiBrushes.Accent;
}

public sealed class TaskMiniViewModel
{
    public string Title { get; init; } = string.Empty;
    public string RewardText { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;
    public bool CanComplete { get; init; }
    public ICommand CompleteCommand { get; init; } = new RelayCommand(() => { });
}

public sealed class ExceptionRowViewModel
{
    public ExceptionRowViewModel(string name, string targetsText, string availabilityText, bool isActive)
    {
        Name = name;
        TargetsText = targetsText;
        AvailabilityText = availabilityText;
        IsActive = isActive;
    }

    public string Name { get; }
    public string TargetsText { get; }
    public string AvailabilityText { get; }
    public bool IsActive { get; }
}
