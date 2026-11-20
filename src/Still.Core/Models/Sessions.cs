namespace Still.Core.Models;

/// <summary>A hard lock that cannot be edited away while it is running.</summary>
public sealed class Lockdown
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset EndsAt { get; set; } = DateTimeOffset.Now.AddMinutes(60);
    public string? Reason { get; set; }
    public List<Target> Targets { get; set; } = new();
    public bool BlockBrowsers { get; set; }
    public bool AllowEarlyExit { get; set; }
    public Guid? ApproverContactId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool IsActiveAt(DateTimeOffset now) => now >= StartsAt && now < EndsAt;

    public TimeSpan Remaining(DateTimeOffset now)
    {
        var remaining = EndsAt - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}

public enum PomodoroPhase
{
    Idle,
    Work,
    Break,
    LongBreak
}

/// <summary>Interval focus timer state that survives restarts.</summary>
public sealed class PomodoroState
{
    public PomodoroPhase Phase { get; set; } = PomodoroPhase.Idle;
    public DateTimeOffset PhaseStartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset PhaseEndsAt { get; set; } = DateTimeOffset.Now;
    public int CompletedWorkCycles { get; set; }
    public int WorkMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int CyclesBeforeLongBreak { get; set; } = 4;
    public string? Label { get; set; }

    public bool IsRunning => Phase != PomodoroPhase.Idle;

    public TimeSpan PhaseDuration => Phase switch
    {
        PomodoroPhase.Work => TimeSpan.FromMinutes(WorkMinutes),
        PomodoroPhase.Break => TimeSpan.FromMinutes(BreakMinutes),
        PomodoroPhase.LongBreak => TimeSpan.FromMinutes(LongBreakMinutes),
        _ => TimeSpan.Zero
    };

    public TimeSpan Remaining(DateTimeOffset now)
    {
        if (Phase == PomodoroPhase.Idle)
        {
            return TimeSpan.Zero;
        }

        var remaining = PhaseEndsAt - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public PomodoroState Clone() => new()
    {
        Phase = Phase,
        PhaseStartedAt = PhaseStartedAt,
        PhaseEndsAt = PhaseEndsAt,
        CompletedWorkCycles = CompletedWorkCycles,
        WorkMinutes = WorkMinutes,
        BreakMinutes = BreakMinutes,
        LongBreakMinutes = LongBreakMinutes,
        CyclesBeforeLongBreak = CyclesBeforeLongBreak,
        Label = Label
    };
}

public sealed class TimerPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Fokus";
    public int WorkMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int CyclesBeforeLongBreak { get; set; } = 4;

    public TimerPreset Clone() => new()
    {
        Id = Id,
        Name = Name,
        WorkMinutes = WorkMinutes,
        BreakMinutes = BreakMinutes,
        LongBreakMinutes = LongBreakMinutes,
        CyclesBeforeLongBreak = CyclesBeforeLongBreak
    };

    public static List<TimerPreset> CreateDefaults() =>
    [
        new() { Name = "Klassisch", WorkMinutes = 25, BreakMinutes = 5, LongBreakMinutes = 15, CyclesBeforeLongBreak = 4 },
        new() { Name = "Tiefe Arbeit", WorkMinutes = 50, BreakMinutes = 10, LongBreakMinutes = 25, CyclesBeforeLongBreak = 3 },
        new() { Name = "Kurz & scharf", WorkMinutes = 15, BreakMinutes = 3, LongBreakMinutes = 10, CyclesBeforeLongBreak = 4 }
    ];
}
