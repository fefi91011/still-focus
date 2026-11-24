using Still.Core.Models;
using Still.Core.Runtime;
using Still.Core.Time;

namespace Still.Core.Engine;

/// <summary>What the user is currently doing, sampled by the platform layer.</summary>
public sealed record ActivitySnapshot(IReadOnlySet<string> ForegroundProcesses)
{
    public static ActivitySnapshot Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public static ActivitySnapshot From(IEnumerable<string> processNames) => new(
        new HashSet<string>(
            processNames.Select(Target.NormalizeProcess).Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase));
}

public sealed record PomodoroStartOptions(
    int WorkMinutes,
    int BreakMinutes,
    int LongBreakMinutes,
    int CyclesBeforeLongBreak,
    string? Label = null);

/// <summary>
/// Pure evaluation of the persisted state. No OS calls happen here, which keeps the
/// complete decision logic testable without Windows.
/// </summary>
public sealed class FocusEngine
{
    private readonly AppState _state;
    private readonly IClock _clock;
    private readonly UsageBook _usage;
    private readonly AppUsageBook _appUsage;
    private DateTimeOffset? _lastEvaluatedAt;
    private DateOnly? _lastTickDay;
    private HashSet<Guid> _previousBlocked = new();

    public FocusEngine(AppState state, IClock clock)
    {
        _state = state;
        _clock = clock;
        _usage = new UsageBook(state);
        _appUsage = new AppUsageBook(state);
    }

    public EngineSnapshot Snapshot { get; private set; } = EngineSnapshot.Empty;

    public bool IsLockdownActive => _state.Lockdown?.IsActiveAt(_clock.Now) == true;

    public void RebuildUsage()
    {
        _usage.Rebuild();
        _appUsage.Rebuild();
    }

    public EngineSnapshot Evaluate(ActivitySnapshot activity)
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var events = new List<EngineEvent>();
        var tickSeconds = ConsumeTick(now, today);

        if (tickSeconds > 0 && activity.ForegroundProcesses.Count > 0)
        {
            Day(today).ScreenSeconds += tickSeconds;

            // Attribute the tick to exactly one process. The set normally has one
            // entry (the foreground window); this keeps totals from double-counting.
            var trackedApp = activity.ForegroundProcesses
                .OrderBy(process => process, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(trackedApp))
            {
                _appUsage.For(today, trackedApp).UsedSeconds += tickSeconds;
            }
        }

        AdvancePomodoro(now, events);

        if (_state.Lockdown is { } expired && !expired.IsActiveAt(now))
        {
            _state.Lockdown = null;
            events.Add(new EngineEvent(EngineEventKind.LockdownEnded, null, "Die Cold-Turkey-Sperre ist beendet."));
        }

        var lockdown = _state.Lockdown;
        var lockdownActive = lockdown?.IsActiveAt(now) == true;
        var activeAllowances = _state.Allowances.Where(a => a.IsActiveAt(now)).ToList();
        var pomodoro = _state.Pomodoro;
        var pomodoroWork = pomodoro?.Phase == PomodoroPhase.Work;

        var statuses = new List<RuleStatus>();
        var blockedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockingRuleIds = new HashSet<Guid>();
        var blockedNow = new HashSet<Guid>();

        foreach (var rule in _state.Rules)
        {
            var targets = rule.DistinctTargets().ToList();
            var scheduleActive = rule.Enabled && rule.Schedule.IsActiveAt(now);
            var pomodoroForced = rule.Enabled && pomodoroWork;
            var armed = scheduleActive || pomodoroForced;
            var usage = _usage.For(rule.Id, today);

            var exceptedTargets = targets
                .Where(t => activeAllowances.Any(a => a.Covers(t)))
                .ToList();
            var effectiveTargets = targets
                .Where(t => !exceptedTargets.Any(t.SameAs))
                .ToList();

            var limitMinutes = rule.DailyLimitMinutes is int limit && limit > 0 ? limit : (int?)null;
            var limitReached = limitMinutes is not null
                               && usage.UsedSeconds >= BudgetSeconds(limitMinutes.Value, usage);

            var activeUse = armed && effectiveTargets.Any(t => IsTargetActive(t, activity));
            if (activeUse && !limitReached && limitMinutes is not null && tickSeconds > 0)
            {
                usage.UsedSeconds += tickSeconds;
                limitReached = usage.UsedSeconds >= BudgetSeconds(limitMinutes.Value, usage);
            }

            var blocked = armed
                          && effectiveTargets.Count > 0
                          && (limitMinutes is null || limitReached);

            if (blocked)
            {
                blockingRuleIds.Add(rule.Id);
                blockedNow.Add(rule.Id);

                foreach (var target in effectiveTargets)
                {
                    AddBlockedTarget(target, blockedProcesses, blockedDomains);
                }

                if (!_previousBlocked.Contains(rule.Id))
                {
                    events.Add(new EngineEvent(EngineEventKind.RuleBlocked, rule.Id, rule.Name));
                    if (limitMinutes is not null && limitReached)
                    {
                        events.Add(new EngineEvent(EngineEventKind.LimitReached, rule.Id, rule.Name));
                    }

                    Day(today).BlockedAttempts++;
                }
            }

            var (nextStart, activeEnd) = rule.Schedule.NextTransition(now);
            if (pomodoroForced && !scheduleActive && pomodoro is not null)
            {
                nextStart = null;
                activeEnd = pomodoro.PhaseEndsAt;
            }

            statuses.Add(new RuleStatus(
                rule.Id,
                rule.Name,
                rule.Enabled,
                armed,
                scheduleActive,
                blocked,
                exceptedTargets.Count > 0,
                limitReached,
                limitMinutes,
                usage.UsedSeconds,
                usage.BonusMinutes,
                usage.GraceBonusMinutes,
                usage.GraceUses,
                rule.MaxGraceUsesPerDay,
                rule.GraceMinutes,
                nextStart,
                activeEnd,
                rule.Schedule.Describe(),
                targets.Count));
        }

        if (lockdownActive && lockdown is not null)
        {
            foreach (var target in lockdown.Targets)
            {
                AddBlockedTarget(target, blockedProcesses, blockedDomains);
            }

            foreach (var rule in _state.Rules.Where(r => r.Enabled))
            {
                blockingRuleIds.Add(rule.Id);
                foreach (var target in rule.Targets)
                {
                    AddBlockedTarget(target, blockedProcesses, blockedDomains);
                }
            }

            if (lockdown.BlockBrowsers)
            {
                foreach (var browser in _state.Settings.BrowserProcesses)
                {
                    blockedProcesses.Add(Target.NormalizeProcess(browser));
                }
            }
        }

        _previousBlocked = blockedNow;

        Snapshot = new EngineSnapshot(
            now,
            statuses,
            blockedProcesses,
            blockedDomains,
            blockingRuleIds,
            lockdownActive ? lockdown : null,
            lockdownActive,
            pomodoro,
            lockdownActive && lockdown?.BlockBrowsers == true,
            events);

        return Snapshot;
    }

    public OperationResult GrantGrace(Guid ruleId, int? minutes = null)
    {
        var now = _clock.Now;
        if (_state.Lockdown?.IsActiveAt(now) == true)
        {
            return OperationResult.Failure("Während Cold Turkey gibt es keine Nachfrist.");
        }

        var rule = _state.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null || !rule.Enabled)
        {
            return OperationResult.Failure("Diese Regel ist nicht aktiv.");
        }

        if (rule.DailyLimitMinutes is not int limit || limit <= 0)
        {
            return OperationResult.Failure("Für Regeln ohne Tageslimit gibt es keine Nachfrist.");
        }

        var status = Snapshot.FindRule(ruleId);
        if (status is null || !status.Blocked || !status.LimitReached)
        {
            return OperationResult.Failure("Das Limit ist noch nicht erreicht.");
        }

        if (status.GraceUsesLeft <= 0)
        {
            return OperationResult.Failure("Die Nachfristen für heute sind aufgebraucht.");
        }

        var add = Math.Clamp(minutes ?? rule.GraceMinutes, 1, 60);
        var usage = _usage.For(ruleId, DateOnly.FromDateTime(now.LocalDateTime));
        usage.GraceBonusMinutes += add;
        usage.GraceUses++;

        var remainingSeconds = BudgetSeconds(limit, usage) - usage.UsedSeconds;
        var remainingMinutes = Math.Max(0, (int)Math.Ceiling(remainingSeconds / 60.0));
        return OperationResult.Success(
            $"{add} Minuten hinzugefügt. Verbleibend heute: {remainingMinutes} Min.");
    }

    public OperationResult CompleteTask(Guid taskId, string? reflection)
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var task = _state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null || !task.Enabled)
        {
            return OperationResult.Failure("Diese Aufgabe ist nicht verfügbar.");
        }

        var doneToday = _state.Completions.Count(c =>
            c.TaskId == task.Id && DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today);
        if (doneToday >= task.MaxPerDay)
        {
            return OperationResult.Failure("Diese Aufgabe ist für heute bereits erledigt.");
        }

        if (task.RequireReflection && string.IsNullOrWhiteSpace(reflection))
        {
            return OperationResult.Failure("Bitte schreibe kurz auf, was du gemacht hast.");
        }

        var minutes = Math.Clamp(task.RewardMinutes, 1, 240);
        var creditedRules = new List<BlockRule>();

        if (task.RuleId is Guid ruleId)
        {
            var rule = _state.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is not null && rule.DailyLimitMinutes is > 0)
            {
                _usage.For(rule.Id, today).BonusMinutes += minutes;
                creditedRules.Add(rule);
            }
        }
        else
        {
            foreach (var rule in _state.Rules.Where(r => r.Enabled && r.DailyLimitMinutes is > 0))
            {
                _usage.For(rule.Id, today).BonusMinutes += minutes;
                creditedRules.Add(rule);
            }
        }

        _state.Completions.Add(new TaskCompletion
        {
            TaskId = task.Id,
            TaskTitle = task.Title,
            CompletedAt = now,
            Reflection = string.IsNullOrWhiteSpace(reflection) ? null : reflection.Trim(),
            MinutesGranted = minutes
        });

        var day = Day(today);
        day.TasksCompleted++;
        day.TaskRewardMinutes += minutes;

        return creditedRules.Count > 0
            ? OperationResult.Success(
                $"„{task.Title}“ erledigt: +{minutes} Minuten für {creditedRules.Count} Regel{(creditedRules.Count == 1 ? "" : "n")}.")
            : OperationResult.Success($"„{task.Title}“ erledigt. Es gibt aktuell kein Tageslimit für eine Gutschrift.");
    }

    public OperationResult StartPomodoro(PomodoroStartOptions options)
    {
        var now = _clock.Now;
        if (_state.Lockdown?.IsActiveAt(now) == true)
        {
            return OperationResult.Failure("Während Cold Turkey läuft bereits eine Sperre.");
        }

        if (_state.Pomodoro is not null)
        {
            return OperationResult.Failure("Es läuft bereits ein Fokus-Timer.");
        }

        var work = Math.Clamp(options.WorkMinutes, 1, 240);
        var pause = Math.Clamp(options.BreakMinutes, 1, 60);
        var longPause = Math.Clamp(options.LongBreakMinutes, 1, 120);
        var cycles = Math.Clamp(options.CyclesBeforeLongBreak, 1, 12);

        _state.Pomodoro = new PomodoroState
        {
            Phase = PomodoroPhase.Work,
            PhaseStartedAt = now,
            PhaseEndsAt = now.AddMinutes(work),
            WorkMinutes = work,
            BreakMinutes = pause,
            LongBreakMinutes = longPause,
            CyclesBeforeLongBreak = cycles,
            Label = string.IsNullOrWhiteSpace(options.Label) ? null : options.Label.Trim()
        };

        return OperationResult.Success("Fokus-Timer gestartet. Alle Regel-Ziele sind jetzt gesperrt.");
    }

    public OperationResult StopPomodoro()
    {
        if (_state.Pomodoro is null)
        {
            return OperationResult.Failure("Es läuft kein Fokus-Timer.");
        }

        var minutes = StopPomodoroInternal(_clock.Now);
        return OperationResult.Success(minutes > 0
            ? $"Timer beendet. {minutes} Fokusminuten wurden gespeichert."
            : "Timer beendet.");
    }

    public OperationResult SkipBreak()
    {
        var now = _clock.Now;
        var pomodoro = _state.Pomodoro;
        if (pomodoro is null || pomodoro.Phase is not (PomodoroPhase.Break or PomodoroPhase.LongBreak))
        {
            return OperationResult.Failure("Gerade läuft keine Pause.");
        }

        pomodoro.Phase = PomodoroPhase.Work;
        pomodoro.PhaseStartedAt = now;
        pomodoro.PhaseEndsAt = now.AddMinutes(pomodoro.WorkMinutes);
        return OperationResult.Success("Pause übersprungen. Nächste Fokusphase läuft.");
    }

    public OperationResult StartLockdown(
        TimeSpan duration,
        string? reason,
        bool blockBrowsers,
        bool allowEarlyExit,
        Guid? approverContactId = null)
    {
        var now = _clock.Now;
        if (_state.Lockdown?.IsActiveAt(now) == true)
        {
            return OperationResult.Failure("Es läuft bereits eine Cold-Turkey-Sperre.");
        }

        var minutes = Math.Clamp((int)Math.Round(duration.TotalMinutes), 5, 10_080);
        var targets = _state.Rules
            .Where(r => r.Enabled)
            .SelectMany(r => r.DistinctTargets())
            .GroupBy(t => ((int)t.Kind, t.Value), StringTupleComparer.Instance)
            .Select(g => g.First().Clone())
            .ToList();

        if (targets.Count == 0 && !blockBrowsers)
        {
            return OperationResult.Failure(
                "Lege zuerst eine Regel mit Zielen an oder wähle „Browser mitsperren“.");
        }

        if (allowEarlyExit && !_state.Contacts.Any(c => c.IsApprover && c.HasCode))
        {
            return OperationResult.Failure(
                "Für eine vorzeitige Freigabe fehlt eine Kontaktperson mit Freigabecode.");
        }

        if (allowEarlyExit && approverContactId is Guid id
            && !_state.Contacts.Any(c => c.Id == id && c.IsApprover && c.HasCode))
        {
            return OperationResult.Failure("Die gewählte Kontaktperson hat keinen Freigabecode.");
        }

        if (_state.Pomodoro is not null)
        {
            StopPomodoroInternal(now);
        }

        _state.Lockdown = new Lockdown
        {
            StartsAt = now,
            EndsAt = now.AddMinutes(minutes),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Targets = targets,
            BlockBrowsers = blockBrowsers,
            AllowEarlyExit = allowEarlyExit,
            ApproverContactId = allowEarlyExit ? approverContactId : null,
            CreatedAt = now
        };

        return OperationResult.Success(
            $"Cold Turkey läuft bis {_state.Lockdown.EndsAt.LocalDateTime:HH:mm} Uhr.");
    }

    public OperationResult EndLockdownEarly(string? approvalCode)
    {
        var now = _clock.Now;
        var lockdown = _state.Lockdown;
        if (lockdown is null || !lockdown.IsActiveAt(now))
        {
            return OperationResult.Failure("Es läuft keine Cold-Turkey-Sperre.");
        }

        if (!lockdown.AllowEarlyExit)
        {
            return OperationResult.Failure("Diese Sperre kann nicht vorzeitig beendet werden.");
        }

        if (!ApprovalCodes.IsUsable(approvalCode))
        {
            return OperationResult.Failure("Bitte gib den Freigabecode einer Kontaktperson ein.");
        }

        var candidates = _state.Contacts.Where(c => c.IsApprover && c.HasCode).ToList();
        if (lockdown.ApproverContactId is Guid approverId)
        {
            candidates = candidates.Where(c => c.Id == approverId).ToList();
        }

        var valid = candidates.Any(c => ApprovalCodes.Verify(approvalCode, c.CodeHash, c.CodeSalt));
        if (!valid)
        {
            return OperationResult.Failure("Der Freigabecode ist nicht korrekt.");
        }

        _state.Lockdown = null;
        return OperationResult.Success("Cold Turkey wurde mit Freigabe beendet.");
    }

    private int StopPomodoroInternal(DateTimeOffset now)
    {
        var pomodoro = _state.Pomodoro;
        if (pomodoro is null)
        {
            return 0;
        }

        var minutes = 0;
        if (pomodoro.Phase == PomodoroPhase.Work)
        {
            var end = now < pomodoro.PhaseEndsAt ? now : pomodoro.PhaseEndsAt;
            if (end > pomodoro.PhaseStartedAt)
            {
                minutes = Math.Max(1, (int)Math.Round((end - pomodoro.PhaseStartedAt).TotalMinutes));
            }
        }

        _state.Pomodoro = null;
        if (minutes > 0)
        {
            AddFocusMinutes(DateOnly.FromDateTime(now.LocalDateTime), minutes);
        }

        return minutes;
    }

    private void AdvancePomodoro(DateTimeOffset now, List<EngineEvent> events)
    {
        var pomodoro = _state.Pomodoro;
        if (pomodoro is null || !pomodoro.IsRunning || now < pomodoro.PhaseEndsAt)
        {
            return;
        }

        if (pomodoro.Phase == PomodoroPhase.Work)
        {
            var endedAt = pomodoro.PhaseEndsAt;
            var minutes = Math.Max(1, (int)Math.Round((endedAt - pomodoro.PhaseStartedAt).TotalMinutes));
            AddFocusMinutes(DateOnly.FromDateTime(endedAt.LocalDateTime), minutes);
            pomodoro.CompletedWorkCycles++;

            var longBreak = pomodoro.CompletedWorkCycles % Math.Max(1, pomodoro.CyclesBeforeLongBreak) == 0;
            pomodoro.Phase = longBreak ? PomodoroPhase.LongBreak : PomodoroPhase.Break;
            events.Add(new EngineEvent(
                EngineEventKind.PomodoroPhaseChanged,
                null,
                longBreak
                    ? "Ein ganzer Block geschafft. Gönn dir eine lange Pause."
                    : "Fokusphase geschafft. Zeit für eine Pause."));
        }
        else
        {
            pomodoro.Phase = PomodoroPhase.Work;
            events.Add(new EngineEvent(
                EngineEventKind.PomodoroPhaseChanged,
                null,
                "Pause vorbei. Die nächste Fokusphase läuft."));
        }

        // Restart from now instead of the exact old end, so a sleeping PC does
        // not silently burn through several phases.
        pomodoro.PhaseStartedAt = now;
        pomodoro.PhaseEndsAt = now + pomodoro.PhaseDuration;
    }

    private bool IsTargetActive(Target target, ActivitySnapshot activity)
    {
        if (target.Kind == TargetKind.Process)
        {
            return activity.ForegroundProcesses.Contains(Target.NormalizeProcess(target.Value));
        }

        return IsBrowserForeground(activity);
    }

    private bool IsBrowserForeground(ActivitySnapshot activity) =>
        activity.ForegroundProcesses.Any(p =>
            _state.Settings.BrowserProcesses.Contains(p, StringComparer.OrdinalIgnoreCase));

    private static void AddBlockedTarget(
        Target target,
        HashSet<string> processes,
        HashSet<string> domains)
    {
        if (target.Kind == TargetKind.Process)
        {
            var name = Target.NormalizeProcess(target.Value);
            if (name.Length > 0)
            {
                processes.Add(name);
            }
        }
        else
        {
            var host = Target.NormalizeHost(target.Value);
            if (host.Length > 0)
            {
                domains.Add(target.IncludeSubdomains ? host : Target.ExactHostPrefix + host);
            }
        }
    }

    private static int BudgetSeconds(int limitMinutes, UsageEntry usage) =>
        Math.Max(0, limitMinutes + usage.BonusMinutes + usage.GraceBonusMinutes) * 60;

    private int ConsumeTick(DateTimeOffset now, DateOnly today)
    {
        if (_lastEvaluatedAt is null)
        {
            _lastEvaluatedAt = now;
            _lastTickDay = today;
            return 0;
        }

        var dayChanged = _lastTickDay != today;
        _lastTickDay = today;
        var seconds = (now - _lastEvaluatedAt.Value).TotalSeconds;
        _lastEvaluatedAt = now;

        // Never count the gap across a day boundary or a sleeping machine.
        if (dayChanged || seconds <= 0)
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(seconds), 0, 5);
    }

    private DayStat Day(DateOnly date)
    {
        var day = _state.Days.FirstOrDefault(d => d.Date == date);
        if (day is null)
        {
            day = new DayStat { Date = date };
            _state.Days.Add(day);
        }

        return day;
    }

    private void AddFocusMinutes(DateOnly date, int minutes)
    {
        if (minutes <= 0)
        {
            return;
        }

        Day(date).FocusMinutes += minutes;
    }

    private sealed class StringTupleComparer : IEqualityComparer<(int Kind, string Value)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((int Kind, string Value) x, (int Kind, string Value) y) =>
            x.Kind == y.Kind && string.Equals(x.Value, y.Value, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int Kind, string Value) obj) =>
            HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Value));
    }
}
