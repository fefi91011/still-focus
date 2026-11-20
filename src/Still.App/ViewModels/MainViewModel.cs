using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Still.Core.Engine;
using Still.Core.Models;
using Still.Core.Runtime;

namespace Still.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly Dictionary<string, IPageViewModel> _pages;
    private readonly Dictionary<string, (string Title, string Subtitle)> _meta;
    private readonly DispatcherTimer _toastTimer;
    private object _currentPage = null!;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _clockText = string.Empty;
    private string _dateText = string.Empty;
    private string _statusText = string.Empty;
    private string _statusDetail = string.Empty;
    private string _nextBlockText = string.Empty;
    private IBrush _statusBrush = UiBrushes.Accent;
    private bool _isCalm = true;
    private bool _isLockdown;
    private bool _isPomodoro;
    private bool _isSettingsActive;
    private bool _isToastVisible;
    private bool _toastIsError;
    private string _toastText = string.Empty;
    private bool _hasEnforcementError;
    private string? _enforcementErrorText;
    private bool _hasPersistenceError;
    private string? _persistenceErrorText;
    private bool _hasLoadWarning;
    private string? _loadWarningText;

    public MainViewModel(StillRuntime runtime, bool demoMode = false)
    {
        Runtime = runtime;
        IsDemo = demoMode;
        Dialog = new DialogViewModel();

        Today = new TodayViewModel(this);
        Rules = new RulesViewModel(this);
        Schedule = new ScheduleViewModel(this);
        Focus = new FocusViewModel(this);
        Tasks = new TasksViewModel(this);
        Contacts = new ContactsViewModel(this);
        Stats = new StatsViewModel(this);
        Settings = new SettingsViewModel(this);

        _pages = new Dictionary<string, IPageViewModel>
        {
            ["today"] = Today,
            ["rules"] = Rules,
            ["schedule"] = Schedule,
            ["focus"] = Focus,
            ["tasks"] = Tasks,
            ["contacts"] = Contacts,
            ["stats"] = Stats,
            ["settings"] = Settings
        };

        _meta = new Dictionary<string, (string, string)>
        {
            ["today"] = ("Heute", "Dein Tag auf einen Blick"),
            ["rules"] = ("Regeln", "Was wann gesperrt wird"),
            ["schedule"] = ("Zeitplan", "Deine Woche als ruhiger Rhythmus"),
            ["focus"] = ("Fokus", "Tiefe Arbeit statt Doomscrolling"),
            ["tasks"] = ("Aufgaben", "Erledigen, dann Zeit zurückverdienen"),
            ["contacts"] = ("Kontakte", "Menschen, die dir helfen, dranzubleiben"),
            ["stats"] = ("Statistik", "Fortschritt, der sichtbar wird"),
            ["settings"] = ("Einstellungen", "Feinjustierung und System")
        };

        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is string route)
            {
                Navigate(route);
            }
        });

        OpenFocusCommand = new RelayCommand(() => Navigate("focus"));
        OpenRulesCommand = new RelayCommand(() => Navigate("rules"));
        OpenStatsCommand = new RelayCommand(() => Navigate("stats"));
        OpenTasksCommand = new RelayCommand(() => Navigate("tasks"));

        NavDaily =
        [
            CreateNav("Heute", "today", "M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z"),
            CreateNav("Fokus", "focus", "M12 21a9 9 0 1 1 0-18a9 9 0 0 1 0 18M12 7v5l3 2"),
            CreateNav("Aufgaben", "tasks", "M4 7l2 2l3-4M13 7h7M4 16l2 2l3-4M13 16h7"),
            CreateNav("Kontakte", "contacts", "M9 11a4 4 0 1 0 0-8a4 4 0 0 0 0 8M2 21c0-4 3-6 7-6s7 2 7 6M17 11a3 3 0 1 0 0-6M17 21c0-2 1-4 3-5")
        ];

        NavPlanning =
        [
            CreateNav("Regeln", "rules", "M12 3l7 3v6c0 4-3 7-7 9c-4-2-7-5-7-9V6z"),
            CreateNav("Zeitplan", "schedule", "M4 6h16v14H4zM8 3v5M16 3v5M4 11h16"),
            CreateNav("Statistik", "stats", "M4 20V11M10 20V4M16 20v-6M22 20H2")
        ];

        SettingsNav = CreateNav("Einstellungen", "settings", "M4 7h16M4 12h16M4 17h16M9 5v4M15 10v4M7 15v4");

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        if (!string.IsNullOrWhiteSpace(runtime.LoadWarning))
        {
            ShowToast(runtime.LoadWarning!, error: true);
        }

        Navigate("today");
        Refresh();
    }

    public StillRuntime Runtime { get; }
    public bool IsDemo { get; }
    public string DemoText => "Demo-Modus – Sperren werden simuliert. Nichts auf diesem System wird blockiert.";
    public string VersionText => "0.1.0";

    public TodayViewModel Today { get; }
    public RulesViewModel Rules { get; }
    public ScheduleViewModel Schedule { get; }
    public FocusViewModel Focus { get; }
    public TasksViewModel Tasks { get; }
    public ContactsViewModel Contacts { get; }
    public StatsViewModel Stats { get; }
    public SettingsViewModel Settings { get; }
    public DialogViewModel Dialog { get; }

    public ObservableCollection<NavItemViewModel> NavDaily { get; }
    public ObservableCollection<NavItemViewModel> NavPlanning { get; }
    public NavItemViewModel SettingsNav { get; }

    public ICommand NavigateCommand { get; }
    public ICommand OpenFocusCommand { get; }
    public ICommand OpenRulesCommand { get; }
    public ICommand OpenStatsCommand { get; }
    public ICommand OpenTasksCommand { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => Set(ref _currentPage, value);
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => Set(ref _subtitle, value);
    }

    public string ClockText
    {
        get => _clockText;
        private set => Set(ref _clockText, value);
    }

    public string DateText
    {
        get => _dateText;
        private set => Set(ref _dateText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => Set(ref _statusDetail, value);
    }

    public string NextBlockText
    {
        get => _nextBlockText;
        private set => Set(ref _nextBlockText, value);
    }

    public IBrush StatusBrush
    {
        get => _statusBrush;
        private set => Set(ref _statusBrush, value);
    }

    public bool IsCalm
    {
        get => _isCalm;
        private set => Set(ref _isCalm, value);
    }

    public bool IsLockdown
    {
        get => _isLockdown;
        private set => Set(ref _isLockdown, value);
    }

    public bool IsPomodoro
    {
        get => _isPomodoro;
        private set => Set(ref _isPomodoro, value);
    }

    public bool IsSettingsActive
    {
        get => _isSettingsActive;
        private set => Set(ref _isSettingsActive, value);
    }

    public bool HasEnforcementError
    {
        get => _hasEnforcementError;
        private set => Set(ref _hasEnforcementError, value);
    }

    public string? EnforcementErrorText
    {
        get => _enforcementErrorText;
        private set => Set(ref _enforcementErrorText, value);
    }

    public bool HasPersistenceError
    {
        get => _hasPersistenceError;
        private set => Set(ref _hasPersistenceError, value);
    }

    public string? PersistenceErrorText
    {
        get => _persistenceErrorText;
        private set => Set(ref _persistenceErrorText, value);
    }

    public bool HasLoadWarning
    {
        get => _hasLoadWarning;
        private set => Set(ref _hasLoadWarning, value);
    }

    public string? LoadWarningText
    {
        get => _loadWarningText;
        private set => Set(ref _loadWarningText, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => Set(ref _isToastVisible, value);
    }

    public bool ToastIsError
    {
        get => _toastIsError;
        private set => Set(ref _toastIsError, value);
    }

    public string ToastText
    {
        get => _toastText;
        private set => Set(ref _toastText, value);
    }

    public void Navigate(string route)
    {
        if (!_pages.TryGetValue(route, out var page) || !_meta.TryGetValue(route, out var meta))
        {
            return;
        }

        CurrentPage = page;
        Title = meta.Title;
        Subtitle = meta.Subtitle;

        foreach (var item in NavDaily.Concat(NavPlanning))
        {
            item.IsActive = item.Route == route;
        }

        SettingsNav.IsActive = route == "settings";
        IsSettingsActive = route == "settings";
        page.Refresh();
    }

    public void Refresh()
    {
        var snapshot = Runtime.Snapshot;
        var now = DateTimeOffset.Now;

        ClockText = now.ToString("HH:mm", German);
        DateText = now.ToString("dddd, d. MMMM", German);
        IsLockdown = snapshot.LockdownActive;
        IsPomodoro = snapshot.PomodoroRunning;
        NextBlockText = BuildNextBlockText(snapshot, now);
    }

    public void ApplySnapshot(EngineSnapshot snapshot)
    {
        var now = DateTimeOffset.Now;

        if (snapshot.LockdownActive && snapshot.Lockdown is { } lockdown)
        {
            var remaining = FormatHours(lockdown.Remaining(now));
            StatusText = $"Cold Turkey · {remaining}";
            StatusDetail = "Änderungen sind bis zum Ende gesperrt";
            StatusBrush = UiBrushes.Danger;
            IsCalm = false;
        }
        else if (snapshot.Pomodoro is { IsRunning: true } pomodoro)
        {
            var remaining = FormatMinutes(pomodoro.Remaining(now));
            IsPomodoro = true;
            if (pomodoro.Phase == PomodoroPhase.Work)
            {
                StatusText = $"Fokus · {remaining}";
                StatusDetail = $"Zyklus {pomodoro.CompletedWorkCycles + 1} läuft";
                StatusBrush = UiBrushes.Accent;
                IsCalm = false;
            }
            else
            {
                StatusText = $"Pause · {remaining}";
                StatusDetail = "Kurz durchatmen";
                StatusBrush = UiBrushes.Info;
                IsCalm = true;
            }
        }
        else if (snapshot.HasBlocks)
        {
            var names = snapshot.Rules
                .Where(r => r.Blocked)
                .Select(r => r.Name)
                .Distinct()
                .Take(2)
                .ToList();
            StatusText = snapshot.BlockingRuleIds.Count == 1
                ? "1 Sperre aktiv"
                : $"{snapshot.BlockingRuleIds.Count} Sperren aktiv";
            StatusDetail = names.Count > 0 ? string.Join(" · ", names) : "Regeln greifen";
            StatusBrush = UiBrushes.Warning;
            IsCalm = false;
        }
        else
        {
            StatusText = "Alles frei";
            StatusDetail = "Keine Regel greift gerade";
            StatusBrush = UiBrushes.Accent;
            IsCalm = true;
        }

        HasEnforcementError = !string.IsNullOrWhiteSpace(Runtime.EnforcementError);
        EnforcementErrorText = Runtime.EnforcementError;
        HasPersistenceError = !string.IsNullOrWhiteSpace(Runtime.PersistenceError);
        PersistenceErrorText = Runtime.PersistenceError;
        HasLoadWarning = !string.IsNullOrWhiteSpace(Runtime.LoadWarning);
        LoadWarningText = Runtime.LoadWarning;
        NextBlockText = BuildNextBlockText(snapshot, now);

        (CurrentPage as IPageViewModel)?.Refresh();
    }

    public void ShowToast(string message, bool error = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ToastText = message;
        ToastIsError = error;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public void Notify(OperationResult result)
    {
        ShowToast(result.Message, error: !result.Ok);
        Refresh();
        (CurrentPage as IPageViewModel)?.Refresh();
    }

    public async Task<DialogResult?> PromptAsync(
        string title,
        string body,
        string label,
        bool password = false,
        bool multiline = false,
        string confirmText = "Bestätigen",
        string? placeholder = null,
        Func<string?, string?>? validate = null,
        string? hint = null)
    {
        return await Dialog.ShowAsync(new DialogOptions
        {
            Title = title,
            Body = body,
            InputLabel = label,
            InputPlaceholder = placeholder,
            IsPassword = password,
            IsMultiline = multiline,
            ConfirmText = confirmText,
            Validate = validate,
            Hint = hint
        });
    }

    public async Task<bool> ConfirmAsync(string title, string body, string confirmText = "Bestätigen")
    {
        var result = await Dialog.ShowAsync(new DialogOptions
        {
            Title = title,
            Body = body,
            ConfirmText = confirmText
        });
        return result?.Confirmed == true;
    }

    public async Task InfoAsync(string title, string body)
    {
        await Dialog.ShowAsync(new DialogOptions
        {
            Title = title,
            Body = body,
            ConfirmText = "OK",
            ShowCancel = false
        });
    }

    public async Task<bool> ConfirmTypedAsync(string title, string body, string word)
    {
        var result = await Dialog.ShowAsync(new DialogOptions
        {
            Title = title,
            Body = body,
            InputLabel = $"Tippe zur Bestätigung „{word}“",
            ConfirmText = "Bestätigen",
            Hint = "Das ist Absicht: Es soll sich nicht versehentlich anfühlen.",
            Validate = input => string.Equals(input?.Trim(), word, StringComparison.OrdinalIgnoreCase)
                ? null
                : "Der Text stimmt nicht."
        });
        return result?.Confirmed == true;
    }

    public async Task<bool> EnsureApprovalAsync(string action = "diese Änderung")
    {
        if (!Runtime.RequiresApprovalForEdits)
        {
            return true;
        }

        var result = await Dialog.ShowAsync(new DialogOptions
        {
            Title = "Freigabe nötig",
            Body = $"Für {action} brauchst du den Code einer Freigabeperson.",
            InputLabel = "Freigabecode",
            InputPlaceholder = "Code der Kontaktperson",
            IsPassword = true,
            ConfirmText = "Freigeben",
            Validate = input => Runtime.ValidateApproval(input) ? null : "Dieser Code stimmt nicht."
        });

        return result?.Confirmed == true;
    }

    public async Task CompleteTaskAsync(Guid taskId, string title, bool requireReflection)
    {
        string? reflection = null;
        if (requireReflection)
        {
            var result = await PromptAsync(
                "Kurz festhalten",
                $"Was hast du für „{title}“ gemacht?",
                "Notiz",
                multiline: true,
                confirmText: "Aufgabe erledigen");

            if (result?.Confirmed != true)
            {
                return;
            }

            reflection = result.Input;
        }

        var completion = Runtime.CompleteTask(taskId, reflection);
        Notify(completion);
    }

    public void GrantGrace(Guid ruleId)
    {
        var result = Runtime.GrantGrace(ruleId);
        Notify(result);
    }

    public void ContactFromBlock()
    {
        var contact = Runtime.State.Contacts
            .Where(c => c.IsApprover && c.HasCode)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Email))
            ?? Runtime.State.Contacts
                .Where(c => c.IsApprover && c.HasCode)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Phone));

        if (contact is null)
        {
            Navigate("contacts");
            ShowToast("Hinterlege bei einer Freigabeperson eine E-Mail-Adresse oder Telefonnummer.", error: true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(contact.Email))
        {
            var subject = Uri.EscapeDataString("Freigabe für meine Fokus-App");
            var body = Uri.EscapeDataString(
                $"Hey {contact.Name},\n\n" +
                "ich bin gerade in Still blockiert und brauche eine Freigabe. " +
                "Kannst du mir kurz den vereinbarten Code schicken?\n\n" +
                "Danke dir!");
            OpenExternal($"mailto:{contact.Email}?subject={subject}&body={body}");
            ShowToast($"E-Mail an {contact.Name} geöffnet.");
            return;
        }

        OpenExternal($"tel:{contact.Phone!.Replace(" ", string.Empty)}");
        ShowToast($"Anruf bei {contact.Name} vorbereitet.");
    }

    public void OpenExternal(string target) => Runtime.OpenExternal(target);

    private NavItemViewModel CreateNav(string label, string route, string icon) =>
        new(label, route, icon, new RelayCommand(() => Navigate(route)));

    private static string BuildNextBlockText(EngineSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.LockdownActive && snapshot.Lockdown is { } lockdown)
        {
            return $"Cold Turkey bis {lockdown.EndsAt.LocalDateTime:HH:mm}";
        }

        if (snapshot.Pomodoro is { IsRunning: true } pomodoro)
        {
            return pomodoro.Phase == PomodoroPhase.Work
                ? $"Sperre bis {pomodoro.PhaseEndsAt.LocalDateTime:HH:mm}"
                : $"Nächste Fokusphase {pomodoro.PhaseEndsAt.LocalDateTime:HH:mm}";
        }

        var next = snapshot.Rules
            .Where(r => r.Enabled && r.BlockStartsAt is not null && r.BlockStartsAt > now)
            .OrderBy(r => r.BlockStartsAt)
            .FirstOrDefault();

        return next is null
            ? "Keine weitere Sperre geplant"
            : $"Nächste Sperre {next.BlockStartsAt!.Value.LocalDateTime:HH:mm} · {next.Name}";
    }

    internal static string FormatMinutes(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00} h"
            : $"{value.Minutes:00}:{value.Seconds:00}";

    internal static string FormatHours(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00} h"
            : $"{value.Minutes}:{value.Seconds:00}";
}
