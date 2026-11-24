using Still.Core.Models;
using Still.Core.Persistence;
using Still.Core.Runtime;

namespace Still.App.Services;

/// <summary>
/// Deterministic sample data so the interface can be reviewed without touching
/// a real Windows system. The platform below intentionally simulates success.
/// </summary>
public static class DemoData
{
    public static StillRuntime CreateRuntime()
    {
        var runtime = new StillRuntime(CreateState(), new DemoStore(), new DemoPlatform());
        runtime.Initialize();
        return runtime;
    }

    public static AppState CreateState()
    {
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var (hash, salt) = ApprovalCodes.Create("ruhig-wald");

        var deepWork = new BlockRule
        {
            Id = Guid.NewGuid(),
            Name = "Deep Work",
            Enabled = true,
            Schedule = Schedule.Always(),
            DailyLimitMinutes = 90,
            GraceMinutes = 5,
            MaxGraceUsesPerDay = 2,
            Note = "Die drei Zeitfresser, jeden Tag.",
            CreatedAt = now.AddDays(-40),
            Targets =
            [
                new Target(TargetKind.Website, "youtube.com", "YouTube"),
                new Target(TargetKind.Website, "reddit.com", "Reddit"),
                new Target(TargetKind.Website, "x.com", "X")
            ]
        };

        var social = new BlockRule
        {
            Id = Guid.NewGuid(),
            Name = "Social Media",
            Enabled = true,
            Schedule = Schedule.Always(),
            DailyLimitMinutes = 30,
            GraceMinutes = 5,
            MaxGraceUsesPerDay = 3,
            Note = "Heute schon aufgebraucht.",
            CreatedAt = now.AddDays(-32),
            Targets =
            [
                new Target(TargetKind.Website, "instagram.com", "Instagram"),
                new Target(TargetKind.Website, "tiktok.com", "TikTok")
            ]
        };

        var work = new BlockRule
        {
            Id = Guid.NewGuid(),
            Name = "Arbeit",
            Enabled = true,
            Schedule = Weekly(DayOfWeek.Monday, DayOfWeek.Friday, 9, 17),
            DailyLimitMinutes = null,
            GraceMinutes = 5,
            MaxGraceUsesPerDay = 0,
            Note = "Ablenkung hat im Büro Pause.",
            CreatedAt = now.AddDays(-21),
            Targets =
            [
                new Target(TargetKind.Process, "discord", "Discord"),
                new Target(TargetKind.Process, "steam", "Steam")
            ]
        };

        var gaming = new BlockRule
        {
            Id = Guid.NewGuid(),
            Name = "Gaming",
            Enabled = true,
            Schedule = Weekly(DayOfWeek.Friday, DayOfWeek.Saturday, 18, 23),
            DailyLimitMinutes = null,
            GraceMinutes = 10,
            MaxGraceUsesPerDay = 1,
            CreatedAt = now.AddDays(-14),
            Targets =
            [
                new Target(TargetKind.Process, "steam", "Steam"),
                new Target(TargetKind.Process, "discord", "Discord")
            ]
        };

        var news = new BlockRule
        {
            Id = Guid.NewGuid(),
            Name = "Nachrichten",
            Enabled = true,
            Schedule = new Schedule
            {
                Mode = ScheduleMode.Weekly,
                Windows = Enum.GetValues<DayOfWeek>()
                    .Select(day => new TimeWindow(day, new TimeOnly(7, 0), new TimeOnly(8, 0)))
                    .ToList()
            },
            DailyLimitMinutes = null,
            GraceMinutes = 5,
            MaxGraceUsesPerDay = 0,
            CreatedAt = now.AddDays(-7),
            Targets =
            [
                new Target(TargetKind.Website, "nzz.ch", "NZZ"),
                new Target(TargetKind.Website, "spiegel.de", "Spiegel")
            ]
        };

        var walk = new FocusTask
        {
            Id = Guid.NewGuid(),
            Title = "20 Minuten spazieren",
            Details = "Frische Luft, kein Handy. Danach fällt der nächste Block leichter.",
            RewardMinutes = 15,
            MaxPerDay = 1,
            RuleId = deepWork.Id,
            RequireReflection = false,
            Enabled = true,
            CreatedAt = now.AddDays(-10)
        };

        var book = new FocusTask
        {
            Id = Guid.NewGuid(),
            Title = "Buch lesen",
            Details = "Mindestens 15 Seiten, danach eine kurze Notiz.",
            RewardMinutes = 20,
            MaxPerDay = 1,
            RuleId = null,
            RequireReflection = true,
            Enabled = true,
            CreatedAt = now.AddDays(-8)
        };

        var kitchen = new FocusTask
        {
            Id = Guid.NewGuid(),
            Title = "Küche aufräumen",
            RewardMinutes = 10,
            MaxPerDay = 2,
            RuleId = null,
            RequireReflection = false,
            Enabled = true,
            CreatedAt = now.AddDays(-5)
        };

        var state = new AppState
        {
            Rules = [deepWork, social, work, gaming, news],
            Tasks = [walk, book, kitchen],
            Contacts =
            [
                new Contact
                {
                    Id = Guid.NewGuid(),
                    Name = "Anna",
                    Email = "anna@example.com",
                    Note = "Meine Schwester. Fragt freundlich, aber bestimmt nach.",
                    IsApprover = true,
                    CodeHash = hash,
                    CodeSalt = salt,
                    CreatedAt = now.AddDays(-30)
                },
                new Contact
                {
                    Id = Guid.NewGuid(),
                    Name = "Jonas",
                    Email = "jonas@example.com",
                    Phone = "+49 170 0000000",
                    Note = "Studienpartner für den Freitagstermin.",
                    IsApprover = false,
                    CreatedAt = now.AddDays(-12)
                }
            ],
            Allowances =
            [
                new Allowance
                {
                    Id = Guid.NewGuid(),
                    Name = "Mittagspause",
                    Enabled = true,
                    Schedule = Weekly(DayOfWeek.Monday, DayOfWeek.Friday, 12, 13),
                    Targets = [new Target(TargetKind.Website, "youtube.com", "YouTube")],
                    Note = "Eine Folge in der Pause ist okay.",
                    CreatedAt = now.AddDays(-20)
                },
                new Allowance
                {
                    Id = Guid.NewGuid(),
                    Name = "Wochenende",
                    Enabled = true,
                    Schedule = new Schedule
                    {
                        Mode = ScheduleMode.Weekly,
                        Windows =
                        [
                            new TimeWindow(DayOfWeek.Saturday, TimeOnly.MinValue, TimeOnly.MinValue),
                            new TimeWindow(DayOfWeek.Sunday, TimeOnly.MinValue, TimeOnly.MinValue)
                        ]
                    },
                    Targets =
                    [
                        new Target(TargetKind.Process, "steam", "Steam"),
                        new Target(TargetKind.Process, "discord", "Discord")
                    ],
                    CreatedAt = now.AddDays(-19)
                }
            ],
            Completions =
            [
                new TaskCompletion
                {
                    Id = Guid.NewGuid(),
                    TaskId = walk.Id,
                    TaskTitle = walk.Title,
                    CompletedAt = At(now, today.ToDateTime(new TimeOnly(9, 10))),
                    MinutesGranted = walk.RewardMinutes
                }
            ],
            Usage =
            [
                new UsageEntry
                {
                    RuleId = deepWork.Id,
                    Date = today,
                    UsedSeconds = 88 * 60,
                    BonusMinutes = 15
                },
                new UsageEntry
                {
                    RuleId = social.Id,
                    Date = today,
                    UsedSeconds = 30 * 60
                }
            ],
            Days = BuildDays(today, now),
            AppUsage = BuildAppUsage(today),
            WebsiteUsage = BuildWebsiteUsage(today),
            Settings = new StillSettings
            {
                CloseToTray = true,
                StartMinimized = false,
                ShowBlockedOverlay = true,
                RequireApprovalForEdits = false,
                RequireApprovalForUnlock = true,
                DefaultGraceMinutes = 5,
                DefaultMaxGraceUses = 2,
                WarningThresholdPercent = 80
            }
        };

        state.Normalize();
        return state;
    }

    private static List<DayStat> BuildDays(DateOnly today, DateTimeOffset now)
    {
        var focus = new[] { 96, 132, 74, 0, 118, 61, 52 };
        var screen = new[] { 214, 268, 176, 0, 240, 138, 84 };
        var blocked = new[] { 2, 1, 4, 0, 2, 3, 2 };
        var tasks = new[] { 1, 2, 0, 0, 1, 1, 1 };
        var rewards = new[] { 15, 30, 0, 0, 15, 10, 15 };

        var days = new List<DayStat>();
        for (var offset = 6; offset >= 0; offset--)
        {
            var date = today.AddDays(-offset);
            var index = 6 - offset;
            days.Add(new DayStat
            {
                Date = date,
                FocusMinutes = focus[index],
                ScreenSeconds = screen[index] * 60,
                BlockedAttempts = blocked[index],
                TasksCompleted = tasks[index],
                TaskRewardMinutes = rewards[index]
            });
        }

        return days;
    }

    private static List<AppUsageEntry> BuildAppUsage(DateOnly today)
    {
        return
        [
            App(today.AddDays(-6), "chrome", 95),
            App(today.AddDays(-6), "code", 65),
            App(today.AddDays(-6), "discord", 30),
            App(today.AddDays(-6), "explorer", 24),

            App(today.AddDays(-5), "chrome", 120),
            App(today.AddDays(-5), "code", 80),
            App(today.AddDays(-5), "discord", 40),
            App(today.AddDays(-5), "spotify", 28),

            App(today.AddDays(-4), "chrome", 60),
            App(today.AddDays(-4), "code", 60),
            App(today.AddDays(-4), "discord", 26),
            App(today.AddDays(-4), "explorer", 30),

            App(today.AddDays(-2), "chrome", 100),
            App(today.AddDays(-2), "code", 75),
            App(today.AddDays(-2), "discord", 35),
            App(today.AddDays(-2), "spotify", 30),

            App(today.AddDays(-1), "chrome", 45),
            App(today.AddDays(-1), "code", 45),
            App(today.AddDays(-1), "discord", 25),
            App(today.AddDays(-1), "explorer", 23),

            App(today, "chrome", 35),
            App(today, "code", 30),
            App(today, "discord", 12),
            App(today, "explorer", 7)
        ];
    }

    private static AppUsageEntry App(DateOnly date, string process, int minutes) => new()
    {
        Date = date,
        ProcessName = process,
        UsedSeconds = minutes * 60
    };

    private static List<WebsiteUsageEntry> BuildWebsiteUsage(DateOnly today)
    {
        return
        [
            Website(today.AddDays(-6), "youtube.com", 32),
            Website(today.AddDays(-6), "github.com", 24),
            Website(today.AddDays(-6), "reddit.com", 18),
            Website(today.AddDays(-6), "x.com", 12),

            Website(today.AddDays(-5), "youtube.com", 41),
            Website(today.AddDays(-5), "github.com", 35),
            Website(today.AddDays(-5), "reddit.com", 22),
            Website(today.AddDays(-5), "nzz.ch", 14),

            Website(today.AddDays(-4), "youtube.com", 18),
            Website(today.AddDays(-4), "github.com", 28),
            Website(today.AddDays(-4), "instagram.com", 9),
            Website(today.AddDays(-4), "tiktok.com", 7),

            Website(today.AddDays(-2), "youtube.com", 36),
            Website(today.AddDays(-2), "github.com", 31),
            Website(today.AddDays(-2), "reddit.com", 16),
            Website(today.AddDays(-2), "spiegel.de", 11),

            Website(today.AddDays(-1), "youtube.com", 22),
            Website(today.AddDays(-1), "github.com", 20),
            Website(today.AddDays(-1), "reddit.com", 8),
            Website(today.AddDays(-1), "nzz.ch", 9),

            Website(today, "youtube.com", 12),
            Website(today, "github.com", 10),
            Website(today, "reddit.com", 8),
            Website(today, "x.com", 5)
        ];
    }

    private static WebsiteUsageEntry Website(DateOnly date, string host, int minutes) => new()
    {
        Date = date,
        Host = host,
        UsedSeconds = minutes * 60
    };

    private static Schedule Weekly(DayOfWeek from, DayOfWeek to, int startHour, int endHour)
    {
        var windows = new List<TimeWindow>();
        for (var day = from; ; day = day == DayOfWeek.Sunday ? DayOfWeek.Monday : day + 1)
        {
            windows.Add(new TimeWindow(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)));
            if (day == to)
            {
                break;
            }
        }

        return new Schedule { Mode = ScheduleMode.Weekly, Windows = windows };
    }

    private static DateTimeOffset At(DateTimeOffset now, DateTime local)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}

public sealed class DemoStore : IStateStore
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "still-demo");

    public string StateFilePath => Path.Combine(DataDirectory, "state.json");

    public AppState Load(out string? warning)
    {
        warning = null;
        return DemoData.CreateState();
    }

    public void Save(AppState state)
    {
        // Demo state intentionally stays in memory.
    }

    public void WriteHeartbeat(DateTimeOffset now)
    {
    }

    public void ClearHeartbeat()
    {
    }
}

public sealed class DemoPlatform : IPlatform
{
    public string DisplayName => "Windows 11 · Demo";
    public bool IsElevated => true;
    public bool IsWindows => true;
    public IProcessController Processes { get; } = new DemoProcesses();
    public IStartupInstaller Startup { get; } = new DemoStartup();

    public void OpenExternal(string target)
    {
    }

    public void RevealPath(string path)
    {
    }

    private sealed class DemoProcesses : IProcessController
    {
        public bool CanBlock => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<ProcessEntry> GetProcesses() => Array.Empty<ProcessEntry>();
        public IReadOnlySet<string> GetForegroundProcesses() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool Terminate(int processId) => true;
    }

    private sealed class DemoStartup : IStartupInstaller
    {
        public bool CanInstall => true;
        public string? UnavailableReason => null;
        public bool IsInstalled => false;

        public OperationResult Install() => OperationResult.Success("Demo: Autostart würde eingerichtet.");

        public OperationResult Remove() => OperationResult.Success("Demo: Autostart würde entfernt.");
    }
}
