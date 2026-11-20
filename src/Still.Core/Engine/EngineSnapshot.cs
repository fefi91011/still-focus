using Still.Core.Models;

namespace Still.Core.Engine;

public enum EngineEventKind
{
    RuleBlocked,
    RuleUnblocked,
    LimitReached,
    GraceGranted,
    TaskCompleted,
    PomodoroPhaseChanged,
    LockdownEnded
}

public sealed record EngineEvent(EngineEventKind Kind, Guid? RuleId, string Message);

public sealed record RuleStatus(
    Guid RuleId,
    string Name,
    bool Enabled,
    bool Armed,
    bool ScheduleActive,
    bool Blocked,
    bool ExceptionActive,
    bool LimitReached,
    int? DailyLimitMinutes,
    int UsedSeconds,
    int BonusMinutes,
    int GraceBonusMinutes,
    int GraceUses,
    int MaxGraceUsesPerDay,
    int GraceMinutes,
    DateTimeOffset? BlockStartsAt,
    DateTimeOffset? BlockEndsAt,
    string ScheduleText,
    int TargetCount)
{
    public int AllowanceSeconds => DailyLimitMinutes is int limit && limit > 0
        ? (limit + BonusMinutes + GraceBonusMinutes) * 60
        : 0;

    public int RemainingSeconds => DailyLimitMinutes is null
        ? 0
        : Math.Max(0, AllowanceSeconds - UsedSeconds);

    public double Fraction => DailyLimitMinutes is null
        ? Blocked ? 1 : 0
        : AllowanceSeconds <= 0 ? 1 : Math.Clamp((double)UsedSeconds / AllowanceSeconds, 0, 1);

    public int GraceUsesLeft => Math.Max(0, MaxGraceUsesPerDay - GraceUses);

    public bool CanGrantGrace => Blocked && LimitReached && GraceUsesLeft > 0 && GraceMinutes > 0;

    public int UsedMinutes => (int)Math.Round(UsedSeconds / 60.0);

    public int RemainingMinutes => (int)Math.Ceiling(RemainingSeconds / 60.0);
}

public sealed record EngineSnapshot(
    DateTimeOffset TakenAt,
    IReadOnlyList<RuleStatus> Rules,
    IReadOnlySet<string> BlockedProcesses,
    IReadOnlySet<string> BlockedDomains,
    IReadOnlySet<Guid> BlockingRuleIds,
    Lockdown? Lockdown,
    bool LockdownActive,
    PomodoroState? Pomodoro,
    bool BlockBrowsers,
    IReadOnlyList<EngineEvent> Events)
{
    public static EngineSnapshot Empty { get; } = new(
        DateTimeOffset.MinValue,
        Array.Empty<RuleStatus>(),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<Guid>(),
        null,
        false,
        null,
        false,
        Array.Empty<EngineEvent>());

    public bool HasBlocks => BlockedProcesses.Count > 0 || BlockedDomains.Count > 0 || BlockBrowsers;

    public bool PomodoroRunning => Pomodoro?.IsRunning == true;

    public RuleStatus? FindRule(Guid id) => Rules.FirstOrDefault(r => r.RuleId == id);
}
