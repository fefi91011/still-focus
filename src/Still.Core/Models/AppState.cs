using System.Text.Json.Serialization;

namespace Still.Core.Models;

public sealed class StillSettings
{
    public bool CloseToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool LaunchAtLogon { get; set; }
    public bool ShowBlockedOverlay { get; set; } = true;
    public bool RequireApprovalForEdits { get; set; }
    public bool RequireApprovalForUnlock { get; set; }
    public int DefaultGraceMinutes { get; set; } = 5;
    public int DefaultMaxGraceUses { get; set; } = 2;
    public int WarningThresholdPercent { get; set; } = 80;
    public bool BlockDohWhenBlocking { get; set; } = true;
    public List<string> BrowserProcesses { get; set; } =
    [
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "zen"
    ];
}

public sealed class UsageEntry
{
    public Guid RuleId { get; set; }
    public DateOnly Date { get; set; }
    public int UsedSeconds { get; set; }
    public int BonusMinutes { get; set; }
    public int GraceBonusMinutes { get; set; }
    public int GraceUses { get; set; }
}

/// <summary>Foreground time for one application on one day, independent of rules.</summary>
public sealed class AppUsageEntry
{
    public string ProcessName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int UsedSeconds { get; set; }
}

public sealed class DayStat
{
    public DateOnly Date { get; set; }
    public int FocusMinutes { get; set; }

    /// <summary>
    /// Seconds any app was in the foreground while Still was running.
    /// Persisted under the legacy key so existing states survive the rename.
    /// </summary>
    [JsonPropertyName("onlineSeconds")]
    public int ScreenSeconds { get; set; }

    public int BlockedAttempts { get; set; }
    public int TasksCompleted { get; set; }
    public int TaskRewardMinutes { get; set; }
}

/// <summary>Everything that is persisted. Kept JSON-friendly and without platform types.</summary>
public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public List<BlockRule> Rules { get; set; } = new();
    public List<Allowance> Allowances { get; set; } = new();
    public List<FocusTask> Tasks { get; set; } = new();
    public List<TaskCompletion> Completions { get; set; } = new();
    public List<Contact> Contacts { get; set; } = new();
    public List<TimerPreset> TimerPresets { get; set; } = TimerPreset.CreateDefaults();
    public List<UsageEntry> Usage { get; set; } = new();
    public List<AppUsageEntry> AppUsage { get; set; } = new();
    public List<DayStat> Days { get; set; } = new();
    public Lockdown? Lockdown { get; set; }
    public PomodoroState? Pomodoro { get; set; }
    public StillSettings Settings { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public static AppState CreateDefault() => new();

    public void Normalize()
    {
        SchemaVersion = 1;
        Rules ??= new();
        Allowances ??= new();
        Tasks ??= new();
        Completions ??= new();
        Contacts ??= new();
        TimerPresets ??= new();
        Usage ??= new();
        AppUsage ??= new();
        Days ??= new();
        Settings ??= new();

        Settings.BrowserProcesses ??= new();
        Settings.BrowserProcesses = Settings.BrowserProcesses
            .Select(Target.NormalizeProcess)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Settings.BrowserProcesses.Count == 0)
        {
            Settings.BrowserProcesses = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi"];
        }

        Settings.DefaultGraceMinutes = Math.Clamp(Settings.DefaultGraceMinutes, 1, 60);
        Settings.DefaultMaxGraceUses = Math.Clamp(Settings.DefaultMaxGraceUses, 0, 10);
        Settings.WarningThresholdPercent = Math.Clamp(Settings.WarningThresholdPercent, 50, 95);

        if (TimerPresets.Count == 0)
        {
            TimerPresets = TimerPreset.CreateDefaults();
        }

        foreach (var rule in Rules)
        {
            if (rule.Id == Guid.Empty)
            {
                rule.Id = Guid.NewGuid();
            }

            rule.Name = string.IsNullOrWhiteSpace(rule.Name) ? "Unbenannte Regel" : rule.Name.Trim();
            rule.Targets ??= new();
            rule.Targets = rule.Targets
                .Where(t => t is not null)
                .Select(t => new Target(t.Kind, t.Value, t.Label))
                .Where(t => t.Value.Length > 0)
                .ToList();
            rule.Schedule ??= Schedule.Always();
            rule.Schedule.Windows ??= new();
            rule.GraceMinutes = Math.Clamp(rule.GraceMinutes, 1, 60);
            rule.MaxGraceUsesPerDay = Math.Clamp(rule.MaxGraceUsesPerDay, 0, 10);
            if (rule.DailyLimitMinutes is <= 0)
            {
                rule.DailyLimitMinutes = null;
            }
        }

        foreach (var allowance in Allowances)
        {
            if (allowance.Id == Guid.Empty)
            {
                allowance.Id = Guid.NewGuid();
            }

            allowance.Name = string.IsNullOrWhiteSpace(allowance.Name) ? "Ausnahme" : allowance.Name.Trim();
            allowance.Targets ??= new();
            allowance.Targets = allowance.Targets
                .Where(t => t is not null)
                .Select(t => new Target(t.Kind, t.Value, t.Label))
                .Where(t => t.Value.Length > 0)
                .ToList();
            allowance.Schedule ??= Schedule.Always();
            allowance.Schedule.Windows ??= new();
        }

        foreach (var task in Tasks)
        {
            if (task.Id == Guid.Empty)
            {
                task.Id = Guid.NewGuid();
            }

            task.Title = string.IsNullOrWhiteSpace(task.Title) ? "Unbenannte Aufgabe" : task.Title.Trim();
            task.RewardMinutes = Math.Clamp(task.RewardMinutes, 1, 240);
            task.MaxPerDay = Math.Clamp(task.MaxPerDay, 1, 20);
        }

        foreach (var contact in Contacts)
        {
            if (contact.Id == Guid.Empty)
            {
                contact.Id = Guid.NewGuid();
            }

            contact.Name = string.IsNullOrWhiteSpace(contact.Name) ? "Kontakt" : contact.Name.Trim();
            if (contact.IsApprover && !contact.HasCode)
            {
                contact.IsApprover = false;
            }
        }

        if (Usage.Count > 1)
        {
            var merged = Usage
                .GroupBy(u => (u.RuleId, u.Date))
                .Select(g => new UsageEntry
                {
                    RuleId = g.Key.RuleId,
                    Date = g.Key.Date,
                    UsedSeconds = g.Sum(x => x.UsedSeconds),
                    BonusMinutes = g.Sum(x => x.BonusMinutes),
                    GraceBonusMinutes = g.Sum(x => x.GraceBonusMinutes),
                    GraceUses = g.Sum(x => x.GraceUses)
                })
                .ToList();
            Usage = merged;
        }

        var usageCutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-400));
        AppUsage = AppUsage
            .Select(entry =>
            {
                entry.ProcessName = Target.NormalizeProcess(entry.ProcessName);
                return entry;
            })
            .Where(entry => entry.ProcessName.Length > 0 && entry.UsedSeconds > 0)
            .GroupBy(entry => (entry.Date, entry.ProcessName))
            .Select(group => new AppUsageEntry
            {
                Date = group.Key.Date,
                ProcessName = group.Key.ProcessName,
                UsedSeconds = group.Sum(entry => entry.UsedSeconds)
            })
            .ToList();
        AppUsage.RemoveAll(entry => entry.Date < usageCutoff);
        Usage.RemoveAll(u => u.Date < usageCutoff);
        Days.RemoveAll(d => d.Date < usageCutoff);
        var completionCutoff = DateTimeOffset.Now.AddDays(-180);
        Completions.RemoveAll(c => c.CompletedAt < completionCutoff);

        if (Pomodoro is not null)
        {
            Pomodoro.WorkMinutes = Math.Clamp(Pomodoro.WorkMinutes, 1, 240);
            Pomodoro.BreakMinutes = Math.Clamp(Pomodoro.BreakMinutes, 1, 60);
            Pomodoro.LongBreakMinutes = Math.Clamp(Pomodoro.LongBreakMinutes, 1, 120);
            Pomodoro.CyclesBeforeLongBreak = Math.Clamp(Pomodoro.CyclesBeforeLongBreak, 1, 12);
            if (Pomodoro.Phase == PomodoroPhase.Idle)
            {
                Pomodoro = null;
            }
        }
    }
}
