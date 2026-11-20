namespace Still.Core.Models;

/// <summary>
/// A block rule. When the schedule is active the targets are blocked. If a daily
/// limit is set, use is allowed until the limit (plus earned bonus and grace time)
/// is used up, then the targets are blocked for the rest of the day.
/// A rule without a limit blocks for the whole active schedule (Cold-Turkey-style).
/// </summary>
public sealed class BlockRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Neue Regel";
    public bool Enabled { get; set; } = true;
    public List<Target> Targets { get; set; } = new();
    public Schedule Schedule { get; set; } = Schedule.Always();
    public int? DailyLimitMinutes { get; set; }
    public int GraceMinutes { get; set; } = 5;
    public int MaxGraceUsesPerDay { get; set; } = 2;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool HasProcessTargets => Targets.Any(t => t.Kind == TargetKind.Process);
    public bool HasWebsiteTargets => Targets.Any(t => t.Kind == TargetKind.Website);

    public IEnumerable<Target> DistinctTargets()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in Targets)
        {
            var key = $"{(int)target.Kind}:{target.Value}";
            if (target.Value.Length > 0 && seen.Add(key))
            {
                yield return target;
            }
        }
    }

    public BlockRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Targets = Targets.Select(t => t.Clone()).ToList(),
        Schedule = Schedule.Clone(),
        DailyLimitMinutes = DailyLimitMinutes,
        GraceMinutes = GraceMinutes,
        MaxGraceUsesPerDay = MaxGraceUsesPerDay,
        Note = Note,
        CreatedAt = CreatedAt
    };
}

/// <summary>An exception that lifts blocks for matching targets while it is active.</summary>
public sealed class Allowance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Ausnahme";
    public bool Enabled { get; set; } = true;
    public List<Target> Targets { get; set; } = new();
    public Schedule Schedule { get; set; } = Schedule.Always();
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool IsActiveAt(DateTimeOffset now) =>
        Enabled
        && Targets.Count > 0
        && (ExpiresAt is null || ExpiresAt > now)
        && Schedule.IsActiveAt(now);

    public bool Covers(Target target) =>
        Targets.Any(t => t.Kind == target.Kind &&
                         (t.Kind == TargetKind.Website
                             ? target.MatchesHost(t.Value) || t.MatchesHost(target.Value)
                             : t.Value.Equals(target.Value, StringComparison.OrdinalIgnoreCase)));

    public Allowance Clone() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Targets = Targets.Select(t => t.Clone()).ToList(),
        Schedule = Schedule.Clone(),
        ExpiresAt = ExpiresAt,
        Note = Note,
        CreatedAt = CreatedAt
    };
}

public sealed record ValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
    public string ErrorText => string.Join(" ", Errors);
    public static ValidationResult Ok { get; } = new(Array.Empty<string>());
}

public static class RuleValidator
{
    public static ValidationResult Validate(BlockRule rule)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            errors.Add("Bitte gib der Regel einen Namen.");
        }

        if (rule.Targets.Count == 0)
        {
            errors.Add("Füge mindestens ein Programm oder eine Website hinzu.");
        }
        else if (rule.Targets.Any(t => string.IsNullOrWhiteSpace(t.Value)))
        {
            errors.Add("Ein Ziel ist leer oder ungültig.");
        }

        if (rule.Schedule.Mode == ScheduleMode.Weekly && rule.Schedule.Windows.Count == 0)
        {
            errors.Add("Der Zeitplan hat keine aktiven Zeitfenster.");
        }

        if (rule.DailyLimitMinutes is <= 0)
        {
            errors.Add("Das Tageslimit muss größer als 0 sein.");
        }

        if (rule.GraceMinutes is < 1 or > 60)
        {
            errors.Add("Die Nachfrist muss zwischen 1 und 60 Minuten liegen.");
        }

        if (rule.MaxGraceUsesPerDay is < 0 or > 10)
        {
            errors.Add("Es sind höchstens 10 Nachfristen pro Tag möglich.");
        }

        return new ValidationResult(errors);
    }

    public static ValidationResult Validate(Allowance allowance)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(allowance.Name))
        {
            errors.Add("Bitte gib der Ausnahme einen Namen.");
        }

        if (allowance.Targets.Count == 0)
        {
            errors.Add("Füge mindestens ein Ziel hinzu, das erlaubt sein soll.");
        }

        if (allowance.ExpiresAt is not null && allowance.ExpiresAt <= DateTimeOffset.Now)
        {
            errors.Add("Das Ablaufdatum liegt in der Vergangenheit.");
        }

        return new ValidationResult(errors);
    }
}
