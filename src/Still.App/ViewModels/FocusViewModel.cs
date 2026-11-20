using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Still.Core.Engine;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class TimerPresetOption
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int WorkMinutes { get; init; }
    public int BreakMinutes { get; init; }
    public int LongBreakMinutes { get; init; }
    public int CyclesBeforeLongBreak { get; init; }
    public string Summary => $"{WorkMinutes} / {BreakMinutes} Min · {CyclesBeforeLongBreak} Runden";
}

/// <summary>A contact that may approve ending a Cold Turkey lock early.</summary>
public sealed class ApproverOption
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class FocusViewModel : ObservableObject, IPageViewModel
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly MainViewModel _main;

    private TimerPresetOption? _selectedPreset;
    private string _workText = "25";
    private string _breakText = "5";
    private string _longBreakText = "15";
    private string _cyclesText = "4";
    private bool _isRunning;
    private bool _isLockdown;
    private bool _canSkip;
    private bool _isBreak;
    private string _phaseLabel = "Bereit";
    private string _timeLeft = "25:00";
    private string _timerSubline = "Bereit, wenn du es bist.";
    private string _completedPhasesText = "0";
    private string _focusTodayText = "0 Min";
    private double _ringValue = 1;
    private string _coldTurkeyMinutes = "60";
    private string _reason = string.Empty;
    private bool _blockBrowsers = true;
    private string _lockdownRemainingText = string.Empty;
    private string _lockdownUntilText = string.Empty;
    private string _approverSignature = "init";
    private bool _hasApprovers;
    private bool _canOfferEarlyExit;
    private ApproverOption? _selectedApprover;
    private bool _canEndLockdownEarly;
    private string _earlyExitHint = string.Empty;
    private string _earlyExitOptionText = string.Empty;

    public FocusViewModel(MainViewModel main)
    {
        _main = main;

        foreach (var preset in main.Runtime.State.TimerPresets)
        {
            Presets.Add(new TimerPresetOption
            {
                Id = preset.Id,
                Name = preset.Name,
                WorkMinutes = preset.WorkMinutes,
                BreakMinutes = preset.BreakMinutes,
                LongBreakMinutes = preset.LongBreakMinutes,
                CyclesBeforeLongBreak = preset.CyclesBeforeLongBreak
            });
        }

        if (Presets.Count == 0)
        {
            Presets.Add(new TimerPresetOption
            {
                Id = Guid.NewGuid(),
                Name = "Klassisch",
                WorkMinutes = 25,
                BreakMinutes = 5,
                LongBreakMinutes = 15,
                CyclesBeforeLongBreak = 4
            });
        }

        StartCommand = new RelayCommand(StartTimer);
        StopCommand = new AsyncRelayCommand(StopTimerAsync, ex => main.ShowToast(ex.Message, error: true));
        SkipCommand = new RelayCommand(SkipBreak);
        SetDurationCommand = new RelayCommand(parameter =>
        {
            if (parameter is string value)
            {
                ColdTurkeyMinutes = value;
            }
        });
        StartLockdownCommand = new AsyncRelayCommand(StartLockdownAsync, ex => main.ShowToast(ex.Message, error: true));
        EndLockdownEarlyCommand = new AsyncRelayCommand(
            EndLockdownEarlyAsync,
            ex => main.ShowToast(ex.Message, error: true));

        SelectedPreset = Presets[0];
        Refresh();
    }

    public ObservableCollection<TimerPresetOption> Presets { get; } = new();
    public ObservableCollection<ApproverOption> Approvers { get; } = new();

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand SetDurationCommand { get; }
    public ICommand StartLockdownCommand { get; }
    public ICommand EndLockdownEarlyCommand { get; }

    public TimerPresetOption? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (Set(ref _selectedPreset, value) && value is not null)
            {
                WorkText = value.WorkMinutes.ToString(German);
                BreakText = value.BreakMinutes.ToString(German);
                LongBreakText = value.LongBreakMinutes.ToString(German);
                CyclesText = value.CyclesBeforeLongBreak.ToString(German);
                if (!IsRunning)
                {
                    TimeLeft = $"{value.WorkMinutes:00}:00";
                }
            }
        }
    }

    public string WorkText
    {
        get => _workText;
        set
        {
            if (Set(ref _workText, value) && !IsRunning)
            {
                TimeLeft = TryGet(WorkText, 25) is var minutes ? $"{minutes:00}:00" : "25:00";
            }
        }
    }

    public string BreakText
    {
        get => _breakText;
        set => Set(ref _breakText, value);
    }

    public string LongBreakText
    {
        get => _longBreakText;
        set => Set(ref _longBreakText, value);
    }

    public string CyclesText
    {
        get => _cyclesText;
        set => Set(ref _cyclesText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                Raise(nameof(CanStart));
                Raise(nameof(CanStop));
            }
        }
    }

    public bool CanStart => !IsRunning && !IsLockdown;
    public bool CanStop => IsRunning;

    public bool IsLockdown
    {
        get => _isLockdown;
        private set
        {
            if (Set(ref _isLockdown, value))
            {
                Raise(nameof(CanStart));
                Raise(nameof(CanStartLockdown));
            }
        }
    }

    public bool CanSkip
    {
        get => _canSkip;
        private set => Set(ref _canSkip, value);
    }

    public bool IsBreak
    {
        get => _isBreak;
        private set => Set(ref _isBreak, value);
    }

    public string PhaseLabel
    {
        get => _phaseLabel;
        private set => Set(ref _phaseLabel, value);
    }

    public string TimeLeft
    {
        get => _timeLeft;
        private set => Set(ref _timeLeft, value);
    }

    public string TimerSubline
    {
        get => _timerSubline;
        private set => Set(ref _timerSubline, value);
    }

    public string CompletedPhasesText
    {
        get => _completedPhasesText;
        private set => Set(ref _completedPhasesText, value);
    }

    public string FocusTodayText
    {
        get => _focusTodayText;
        private set => Set(ref _focusTodayText, value);
    }

    public double RingValue
    {
        get => _ringValue;
        private set => Set(ref _ringValue, value);
    }

    public string ColdTurkeyMinutes
    {
        get => _coldTurkeyMinutes;
        set => Set(ref _coldTurkeyMinutes, value);
    }

    public string Reason
    {
        get => _reason;
        set => Set(ref _reason, value);
    }

    public bool BlockBrowsers
    {
        get => _blockBrowsers;
        set => Set(ref _blockBrowsers, value);
    }

    public bool CanStartLockdown => !IsLockdown;

    public bool HasApprovers
    {
        get => _hasApprovers;
        private set => Set(ref _hasApprovers, value);
    }

    /// <summary>
    /// True when a Cold Turkey lock may be ended early with an approver code.
    /// Driven by the global "Vorzeitige Freigabe" setting plus a contact with code.
    /// </summary>
    public bool CanOfferEarlyExit
    {
        get => _canOfferEarlyExit;
        private set => Set(ref _canOfferEarlyExit, value);
    }

    public ApproverOption? SelectedApprover
    {
        get => _selectedApprover;
        set => Set(ref _selectedApprover, value);
    }

    public string EarlyExitOptionText
    {
        get => _earlyExitOptionText;
        private set => Set(ref _earlyExitOptionText, value);
    }

    public bool CanEndLockdownEarly
    {
        get => _canEndLockdownEarly;
        private set => Set(ref _canEndLockdownEarly, value);
    }

    public string EarlyExitHint
    {
        get => _earlyExitHint;
        private set => Set(ref _earlyExitHint, value);
    }

    public string LockdownRemainingText
    {
        get => _lockdownRemainingText;
        private set => Set(ref _lockdownRemainingText, value);
    }

    public string LockdownUntilText
    {
        get => _lockdownUntilText;
        private set => Set(ref _lockdownUntilText, value);
    }

    public string TimerHint =>
        "Während der Arbeit sind alle Ziele deiner Regeln gesperrt. In der Pause greifen nur deine Zeitpläne.";

    public string LockdownDescription =>
        "Hart gesperrt, ohne Pause und ohne Nachfrist. Updates, Neustarts und der Watchdog halten die Sperre. " +
        "Wähle bewusst, was du jetzt wirklich brauchst.";

    public void Refresh()
    {
        var state = _main.Runtime.State;
        var snapshot = _main.Runtime.Snapshot;
        var now = DateTimeOffset.Now;

        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var day = state.Days.FirstOrDefault(d => d.Date == today);
        FocusTodayText = $"{day?.FocusMinutes ?? 0} Min";
        CompletedPhasesText = (state.Pomodoro?.CompletedWorkCycles ?? 0).ToString(German);

        RefreshApprovers();

        IsLockdown = snapshot.LockdownActive;
        CanOfferEarlyExit = !snapshot.LockdownActive
                            && HasApprovers
                            && _main.Runtime.RequiresApprovalForUnlock;
        EarlyExitOptionText = SelectedApprover?.Id is null
            ? "Cold Turkey lässt sich nur mit dem Code einer Freigabeperson vorzeitig beenden."
            : $"Cold Turkey lässt sich nur mit dem Code von {SelectedApprover.Name} vorzeitig beenden.";

        CanEndLockdownEarly = snapshot.LockdownActive && snapshot.Lockdown?.AllowEarlyExit == true;
        if (snapshot.LockdownActive && snapshot.Lockdown is { } activeLockdown)
        {
            LockdownRemainingText = MainViewModel.FormatHours(activeLockdown.Remaining(now));
            LockdownUntilText = $"bis {activeLockdown.EndsAt.LocalDateTime:HH:mm} Uhr";

            var approverName = activeLockdown.ApproverContactId is Guid approverId
                ? state.Contacts.FirstOrDefault(c => c.Id == approverId)?.Name
                : null;
            EarlyExitHint = approverName is null
                ? "Vorzeitige Freigabe: Code einer Freigabeperson nötig."
                : $"Vorzeitige Freigabe: nur mit dem Code von {approverName}.";
        }
        else
        {
            EarlyExitHint = string.Empty;
        }

        var pomodoro = snapshot.Pomodoro;
        IsRunning = pomodoro is { IsRunning: true };

        if (pomodoro is { IsRunning: true })
        {
            var remaining = pomodoro.Remaining(now);
            IsBreak = pomodoro.Phase is PomodoroPhase.Break or PomodoroPhase.LongBreak;
            CanSkip = IsBreak;
            PhaseLabel = pomodoro.Phase switch
            {
                PomodoroPhase.Work => "Arbeit",
                PomodoroPhase.Break => "Pause",
                PomodoroPhase.LongBreak => "Lange Pause",
                _ => "Bereit"
            };
            TimeLeft = FormatTimer(remaining);
            RingValue = pomodoro.PhaseDuration.TotalSeconds <= 0
                ? 0
                : Math.Clamp(remaining.TotalSeconds / pomodoro.PhaseDuration.TotalSeconds, 0, 1);
            TimerSubline = pomodoro.Phase == PomodoroPhase.Work
                ? $"Zyklus {pomodoro.CompletedWorkCycles + 1} · {pomodoro.WorkMinutes} Min tiefe Arbeit"
                : $"Pause · danach {pomodoro.WorkMinutes} Min Fokus";
            CompletedPhasesText = pomodoro.CompletedWorkCycles.ToString(German);
        }
        else
        {
            IsBreak = false;
            CanSkip = false;
            PhaseLabel = IsLockdown ? "Pausiert" : "Bereit";
            TimeLeft = $"{TryGet(WorkText, 25):00}:00";
            RingValue = 1;
            TimerSubline = IsLockdown
                ? "Cold Turkey läuft bereits."
                : "Bereit, wenn du es bist.";
        }
    }

    private void StartTimer()
    {
        if (!TryGetRange(WorkText, 1, 240, out var work))
        {
            _main.ShowToast("Arbeitsphase: 1 bis 240 Minuten.", error: true);
            return;
        }

        if (!TryGetRange(BreakText, 1, 60, out var pause))
        {
            _main.ShowToast("Pause: 1 bis 60 Minuten.", error: true);
            return;
        }

        if (!TryGetRange(LongBreakText, 1, 120, out var longPause))
        {
            _main.ShowToast("Lange Pause: 1 bis 120 Minuten.", error: true);
            return;
        }

        if (!TryGetRange(CyclesText, 1, 12, out var cycles))
        {
            _main.ShowToast("Runden: 1 bis 12.", error: true);
            return;
        }

        var result = _main.Runtime.StartPomodoro(new PomodoroStartOptions(work, pause, longPause, cycles));
        _main.Notify(result);
    }

    private async Task StopTimerAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        if (!await _main.ConfirmAsync(
                "Fokus-Timer beenden",
                "Die aktuelle Phase wird beendet. Erreichte Fokusminuten werden trotzdem gespeichert.",
                "Beenden"))
        {
            return;
        }

        var result = _main.Runtime.StopPomodoro();
        _main.Notify(result);
    }

    private void SkipBreak()
    {
        var result = _main.Runtime.SkipBreak();
        _main.Notify(result);
    }

    private async Task StartLockdownAsync()
    {
        if (!TryGetRange(ColdTurkeyMinutes, 5, 10_080, out var minutes))
        {
            _main.ShowToast("Die Dauer muss zwischen 5 Minuten und 7 Tagen liegen.", error: true);
            return;
        }

        var state = _main.Runtime.State;
        var targetCount = state.Rules
            .Where(r => r.Enabled)
            .SelectMany(r => r.DistinctTargets())
            .GroupBy(t => ((int)t.Kind, t.Value))
            .Count();

        if (targetCount == 0 && !BlockBrowsers)
        {
            _main.ShowToast("Lege zuerst eine Regel an oder sperre die Browser mit.", error: true);
            return;
        }

        var browserText = BlockBrowsers ? " Browser werden zusätzlich komplett gesperrt." : string.Empty;
        var earlyExit = CanOfferEarlyExit;
        var exitText = earlyExit
            ? SelectedApprover?.Id is null
                ? " Vorzeitig beenden geht nur mit dem Code einer Freigabeperson."
                : $" Vorzeitig beenden geht nur mit dem Code von {SelectedApprover.Name}."
            : " Du kannst diese Sperre nicht vorzeitig beenden.";

        var confirmed = await _main.ConfirmTypedAsync(
            "Cold Turkey starten",
            $"{minutes} Minuten ohne Ausweg. {targetCount} Ziele werden gesperrt.{browserText}{exitText}",
            "COLD TURKEY");

        if (!confirmed)
        {
            return;
        }

        var result = _main.Runtime.StartLockdown(
            TimeSpan.FromMinutes(minutes),
            string.IsNullOrWhiteSpace(Reason) ? null : Reason,
            BlockBrowsers,
            allowEarlyExit: earlyExit,
            approverContactId: earlyExit ? SelectedApprover?.Id : null);

        _main.Notify(result);
    }

    private async Task EndLockdownEarlyAsync()
    {
        var lockdown = _main.Runtime.State.Lockdown;
        if (lockdown is null || !CanEndLockdownEarly)
        {
            return;
        }

        var approverName = lockdown.ApproverContactId is Guid approverId
            ? _main.Runtime.State.Contacts.FirstOrDefault(c => c.Id == approverId)?.Name
            : null;
        var body = approverName is null
            ? "Diese Sperre lässt sich nur mit dem Code einer Freigabeperson beenden."
            : $"Diese Sperre lässt sich nur mit dem Code von {approverName} beenden.";

        var input = await _main.PromptAsync(
            "Cold Turkey beenden",
            body,
            "Freigabecode",
            password: true,
            confirmText: "Freigeben",
            placeholder: "Code der Kontaktperson");

        if (input?.Confirmed != true)
        {
            return;
        }

        var result = _main.Runtime.EndLockdownEarly(input.Input);
        _main.Notify(result);
    }

    private void RefreshApprovers()
    {
        var approvers = _main.Runtime.State.Contacts
            .Where(c => c.IsApprover && c.HasCode)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var signature = string.Join("|", approvers.Select(c => $"{c.Id}:{c.Name}"));
        if (signature == _approverSignature)
        {
            return;
        }

        _approverSignature = signature;
        var previous = SelectedApprover?.Id;
        Approvers.Clear();
        Approvers.Add(new ApproverOption { Id = null, Name = "Alle Freigabepersonen" });
        foreach (var contact in approvers)
        {
            Approvers.Add(new ApproverOption { Id = contact.Id, Name = contact.Name });
        }

        SelectedApprover = Approvers.FirstOrDefault(a => a.Id == previous) ?? Approvers[0];
        HasApprovers = approvers.Count > 0;
    }

    private static string FormatTimer(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";

    private static int TryGet(string text, int fallback) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, German, out var value) ? value : fallback;

    private static bool TryGetRange(string text, int min, int max, out int value)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, German, out value) && value >= min && value <= max)
        {
            return true;
        }

        value = min;
        return false;
    }
}
