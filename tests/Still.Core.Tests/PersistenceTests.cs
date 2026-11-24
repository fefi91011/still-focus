using Still.Core.Models;
using Still.Core.Persistence;
using Xunit;

namespace Still.Core.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void State_Roundtrips_Through_Json()
    {
        var directory = Path.Combine(Path.GetTempPath(), "still-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStateStore(directory);
            var rule = new BlockRule
            {
                Name = "YouTube",
                Schedule = new Schedule
                {
                    Mode = ScheduleMode.Weekly,
                    Windows = [new TimeWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 30))]
                },
                Targets = [new Target(TargetKind.Website, "youtube.com")],
                DailyLimitMinutes = 45
            };
            var state = new AppState { Rules = [rule] };
            state.Tasks.Add(new FocusTask { Title = "Lesen", RewardMinutes = 20 });
            state.Usage.Add(new UsageEntry
            {
                RuleId = rule.Id,
                Date = DateOnly.FromDateTime(DateTime.Today),
                UsedSeconds = 600,
                GraceUses = 1
            });
            state.AppUsage.Add(new AppUsageEntry
            {
                Date = DateOnly.FromDateTime(DateTime.Today),
                ProcessName = "chrome",
                UsedSeconds = 120
            });
            state.WebsiteUsage.Add(new WebsiteUsageEntry
            {
                Date = DateOnly.FromDateTime(DateTime.Today),
                Host = "youtube.com",
                UsedSeconds = 240
            });
            state.Normalize();

            store.Save(state);
            var loaded = store.Load(out var warning);

            Assert.Null(warning);
            Assert.Single(loaded.Rules);
            Assert.Equal("YouTube", loaded.Rules[0].Name);
            Assert.Equal(45, loaded.Rules[0].DailyLimitMinutes);
            Assert.True(loaded.Rules[0].Schedule.IsActiveAt(TestTime.Local(2026, 3, 2, 9, 30)));
            Assert.Single(loaded.Tasks);
            Assert.Equal(600, loaded.Usage[0].UsedSeconds);
            Assert.Equal(1, loaded.Usage[0].GraceUses);
            Assert.Single(loaded.AppUsage);
            Assert.Equal("chrome", loaded.AppUsage[0].ProcessName);
            Assert.Equal(120, loaded.AppUsage[0].UsedSeconds);
            Assert.Single(loaded.WebsiteUsage);
            Assert.Equal("youtube.com", loaded.WebsiteUsage[0].Host);
            Assert.Equal(240, loaded.WebsiteUsage[0].UsedSeconds);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Broken_State_Falls_Back_To_The_Backup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "still-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStateStore(directory);
            var first = new AppState
            {
                Rules = [new BlockRule { Name = "Original", Targets = [new Target(TargetKind.Process, "chrome")] }]
            };
            first.Normalize();
            store.Save(first);

            var second = new AppState
            {
                Rules = [new BlockRule { Name = "Neuer Stand", Targets = [new Target(TargetKind.Process, "firefox")] }]
            };
            second.Normalize();
            store.Save(second);

            File.WriteAllText(store.StateFilePath, "{ definitely not json");
            var loaded = store.Load(out var warning);

            Assert.NotNull(warning);
            Assert.Contains("Sicherung", warning!);
            Assert.Equal("Original", loaded.Rules[0].Name);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Normalize_Clamps_Invalid_Values_And_Removes_Duplicate_Usage()
    {
        var rule = new BlockRule
        {
            Name = "  ",
            DailyLimitMinutes = 0,
            GraceMinutes = 999,
            MaxGraceUsesPerDay = -4,
            Targets = [new Target(TargetKind.Process, "chrome")]
        };
        var state = new AppState
        {
            Rules = [rule],
            Usage =
            [
                new UsageEntry { RuleId = rule.Id, Date = DateOnly.FromDateTime(DateTime.Today), UsedSeconds = 60 },
                new UsageEntry { RuleId = rule.Id, Date = DateOnly.FromDateTime(DateTime.Today), UsedSeconds = 120 }
            ],
            AppUsage =
            [
                new AppUsageEntry { Date = DateOnly.FromDateTime(DateTime.Today), ProcessName = "chrome.exe", UsedSeconds = 60 },
                new AppUsageEntry { Date = DateOnly.FromDateTime(DateTime.Today), ProcessName = "chrome", UsedSeconds = 120 }
            ],
            WebsiteUsage =
            [
                new WebsiteUsageEntry { Date = DateOnly.FromDateTime(DateTime.Today), Host = "WWW.YouTube.com", UsedSeconds = 60 },
                new WebsiteUsageEntry { Date = DateOnly.FromDateTime(DateTime.Today), Host = "youtube.com", UsedSeconds = 120 },
                new WebsiteUsageEntry { Date = DateOnly.FromDateTime(DateTime.Today), Host = " ", UsedSeconds = 90 },
                new WebsiteUsageEntry { Date = DateOnly.FromDateTime(DateTime.Today), Host = "reddit.com", UsedSeconds = 0 }
            ]
        };

        state.Normalize();

        Assert.Equal("Unbenannte Regel", state.Rules[0].Name);
        Assert.Null(state.Rules[0].DailyLimitMinutes);
        Assert.Equal(60, state.Rules[0].GraceMinutes);
        Assert.Equal(0, state.Rules[0].MaxGraceUsesPerDay);
        Assert.Single(state.Usage);
        Assert.Equal(180, state.Usage[0].UsedSeconds);
        Assert.Single(state.AppUsage);
        Assert.Equal("chrome", state.AppUsage[0].ProcessName);
        Assert.Equal(180, state.AppUsage[0].UsedSeconds);
        Assert.Single(state.WebsiteUsage);
        Assert.Equal("youtube.com", state.WebsiteUsage[0].Host);
        Assert.Equal(180, state.WebsiteUsage[0].UsedSeconds);
    }
}
