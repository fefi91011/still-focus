using Still.Core.Engine;
using Still.Core.Models;
using Xunit;

namespace Still.Core.Tests;

public sealed class EngineTests
{
    private static BlockRule Rule(
        int? limit = null,
        Schedule? schedule = null,
        int grace = 5,
        int maxGrace = 2,
        params Target[] targets) => new()
    {
        Name = "Test-Regel",
        Targets = targets.Length > 0 ? targets.ToList() : [new Target(TargetKind.Process, "chrome")],
        Schedule = schedule ?? Schedule.Always(),
        DailyLimitMinutes = limit,
        GraceMinutes = grace,
        MaxGraceUsesPerDay = maxGrace
    };

    private static void Tick(FocusEngine engine, FakeClock clock, int seconds, params string[] active)
    {
        var activity = ActivitySnapshot.From(active);
        for (var i = 0; i < seconds; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            engine.Evaluate(activity);
        }
    }

    [Fact]
    public void Limited_Rule_Blocks_After_The_Budget_Is_Used()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(limit: 1);
        var state = new AppState { Rules = [rule] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Tick(engine, clock, 59, "chrome");
        Assert.False(engine.Snapshot.FindRule(rule.Id)!.Blocked);

        Tick(engine, clock, 1, "chrome");
        var status = engine.Snapshot.FindRule(rule.Id)!;
        Assert.True(status.Blocked);
        Assert.Equal(60, status.UsedSeconds);
        Assert.Contains("chrome", engine.Snapshot.BlockedProcesses);
    }

    [Fact]
    public void Usage_Only_Counts_When_The_Target_Is_In_The_Foreground()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(limit: 30);
        var state = new AppState { Rules = [rule] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["explorer"]));
        Tick(engine, clock, 30, "explorer");
        Assert.Equal(0, engine.Snapshot.FindRule(rule.Id)!.UsedSeconds);

        Tick(engine, clock, 30, "chrome");
        Assert.Equal(30, engine.Snapshot.FindRule(rule.Id)!.UsedSeconds);
    }

    [Fact]
    public void Screen_Time_Counts_While_Any_App_Is_In_The_Foreground()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var state = new AppState();
        state.Normalize();
        var engine = new FocusEngine(state, clock);
        var today = DateOnly.FromDateTime(clock.Now.LocalDateTime);

        engine.Evaluate(ActivitySnapshot.From(["explorer"]));
        Tick(engine, clock, 30, "explorer");
        Assert.Equal(30, state.Days.Single(d => d.Date == today).ScreenSeconds);

        Tick(engine, clock, 45, "chrome");
        Assert.Equal(75, state.Days.Single(d => d.Date == today).ScreenSeconds);

        Tick(engine, clock, 15, "explorer");
        Assert.Equal(90, state.Days.Single(d => d.Date == today).ScreenSeconds);

        Tick(engine, clock, 10);
        Assert.Equal(90, state.Days.Single(d => d.Date == today).ScreenSeconds);
    }

    [Fact]
    public void App_Usage_Tracks_Every_Foreground_App_Independent_Of_Rules()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var state = new AppState();
        state.Normalize();
        var engine = new FocusEngine(state, clock);
        var today = DateOnly.FromDateTime(clock.Now.LocalDateTime);

        engine.Evaluate(ActivitySnapshot.From(["explorer"]));
        Tick(engine, clock, 30, "explorer");
        Tick(engine, clock, 45, "chrome");
        Tick(engine, clock, 15, "explorer");

        Assert.Equal(45, state.AppUsage.Single(entry =>
            entry.Date == today && entry.ProcessName == "explorer").UsedSeconds);
        Assert.Equal(45, state.AppUsage.Single(entry =>
            entry.Date == today && entry.ProcessName == "chrome").UsedSeconds);
        Assert.Equal(90, state.Days.Single(day => day.Date == today).ScreenSeconds);
    }

    [Fact]
    public void Screen_Time_Starts_At_Zero_On_A_New_Day()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 23, 58));
        var state = new AppState();
        state.Normalize();
        var engine = new FocusEngine(state, clock);
        var firstDay = DateOnly.FromDateTime(clock.Now.LocalDateTime);

        engine.Evaluate(ActivitySnapshot.From(["explorer"]));
        Tick(engine, clock, 60, "explorer");
        Assert.Equal(60, state.Days.Single(d => d.Date == firstDay).ScreenSeconds);

        clock.Now = TestTime.Local(2026, 3, 3, 0, 1);
        engine.Evaluate(ActivitySnapshot.From(["explorer"]));
        var secondDay = DateOnly.FromDateTime(clock.Now.LocalDateTime);
        Assert.Equal(0, state.Days.FirstOrDefault(d => d.Date == secondDay)?.ScreenSeconds ?? 0);

        Tick(engine, clock, 10, "explorer");
        Assert.Equal(10, state.Days.Single(d => d.Date == secondDay).ScreenSeconds);
        Assert.Equal(60, state.Days.Single(d => d.Date == firstDay).ScreenSeconds);
    }

    [Fact]
    public void Budget_Resets_On_The_Next_Day()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 22, 0));
        var rule = Rule(limit: 1);
        var state = new AppState { Rules = [rule] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Tick(engine, clock, 60, "chrome");
        Assert.True(engine.Snapshot.FindRule(rule.Id)!.Blocked);

        clock.Now = TestTime.Local(2026, 3, 3, 0, 1);
        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        var status = engine.Snapshot.FindRule(rule.Id)!;
        Assert.False(status.Blocked);
        Assert.Equal(0, status.UsedSeconds);
    }

    [Fact]
    public void Grace_Extends_The_Budget_And_Is_Counted()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(limit: 1, grace: 5, maxGrace: 2);
        var state = new AppState { Rules = [rule] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Tick(engine, clock, 60, "chrome");
        Assert.True(engine.Snapshot.FindRule(rule.Id)!.Blocked);

        var result = engine.GrantGrace(rule.Id);
        Assert.True(result.Ok);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        var status = engine.Snapshot.FindRule(rule.Id)!;
        Assert.False(status.Blocked);
        Assert.Equal(1, status.GraceUses);
        Assert.Equal(300, status.RemainingSeconds);
        Assert.Equal(5, status.GraceBonusMinutes);
    }

    [Fact]
    public void Grace_Is_Unavailable_When_Disabled()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(limit: 1, grace: 5, maxGrace: 0);
        var state = new AppState { Rules = [rule] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Tick(engine, clock, 60, "chrome");

        var result = engine.GrantGrace(rule.Id);
        Assert.False(result.Ok);
        Assert.Contains("aufgebraucht", result.Message);
    }

    [Fact]
    public void Exception_Overrides_A_Block_Without_Using_The_Budget()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(limit: 1);
        var state = new AppState
        {
            Rules = [rule],
            Allowances =
            [
                new Allowance
                {
                    Name = "Arbeit",
                    Targets = [new Target(TargetKind.Process, "chrome")],
                    Schedule = Schedule.Always()
                }
            ]
        };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Tick(engine, clock, 120, "chrome");
        var status = engine.Snapshot.FindRule(rule.Id)!;

        Assert.False(status.Blocked);
        Assert.True(status.ExceptionActive);
        Assert.Equal(0, status.UsedSeconds);
        Assert.Empty(engine.Snapshot.BlockedProcesses);
    }

    [Fact]
    public void Rule_Without_Limit_Blocks_While_The_Schedule_Is_Active()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(schedule: new Schedule
        {
            Mode = ScheduleMode.Weekly,
            Windows = [new TimeWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0))]
        });
        var state = new AppState { Rules = [rule] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.Contains("chrome", engine.Snapshot.BlockedProcesses);

        clock.Now = TestTime.Local(2026, 3, 2, 11, 30);
        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.Empty(engine.Snapshot.BlockedProcesses);
    }

    [Fact]
    public void Completed_Task_Grants_Bonus_Time_Once_Per_Day()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(limit: 30);
        var task = new FocusTask { Title = "Lesen", RewardMinutes = 15, MaxPerDay = 1 };
        var state = new AppState { Rules = [rule], Tasks = [task] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        var first = engine.CompleteTask(task.Id, null);
        Assert.True(first.Ok);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        var status = engine.Snapshot.FindRule(rule.Id)!;
        Assert.Equal(15, status.BonusMinutes);
        Assert.Equal(45 * 60, status.RemainingSeconds);

        var second = engine.CompleteTask(task.Id, null);
        Assert.False(second.Ok);
    }

    [Fact]
    public void Reflection_Can_Be_Required()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var task = new FocusTask { Title = "Sport", RewardMinutes = 10, RequireReflection = true };
        var state = new AppState { Tasks = [task] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        Assert.False(engine.CompleteTask(task.Id, "  ").Ok);
        Assert.True(engine.CompleteTask(task.Id, "20 Liegestütze").Ok);
    }

    [Fact]
    public void Pomodoro_Blocks_During_Work_And_Suspends_On_The_Break()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(schedule: new Schedule
        {
            Mode = ScheduleMode.Weekly,
            Windows = [new TimeWindow(DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(11, 0))]
        });
        var state = new AppState { Rules = [rule] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.Empty(engine.Snapshot.BlockedProcesses);

        Assert.True(engine.StartPomodoro(new PomodoroStartOptions(25, 5, 15, 4)).Ok);
        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.Contains("chrome", engine.Snapshot.BlockedProcesses);

        clock.Advance(TimeSpan.FromMinutes(25));
        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.Equal(PomodoroPhase.Break, engine.Snapshot.Pomodoro!.Phase);
        Assert.Empty(engine.Snapshot.BlockedProcesses);
        Assert.Equal(25, state.Days.Sum(d => d.FocusMinutes));

        clock.Advance(TimeSpan.FromMinutes(5));
        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.Equal(PomodoroPhase.Work, engine.Snapshot.Pomodoro!.Phase);
        Assert.Contains("chrome", engine.Snapshot.BlockedProcesses);
    }

    [Fact]
    public void Stopping_A_Pomodoro_Keeps_Partial_Focus_Minutes()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var state = new AppState();
        var engine = new FocusEngine(state, clock);

        engine.StartPomodoro(new PomodoroStartOptions(25, 5, 15, 4));
        clock.Advance(TimeSpan.FromMinutes(10));
        var result = engine.StopPomodoro();

        Assert.True(result.Ok);
        Assert.Contains("10", result.Message);
        Assert.Equal(10, state.Days.Sum(d => d.FocusMinutes));
        Assert.Null(state.Pomodoro);
    }

    [Fact]
    public void Lockdown_Ignores_Exceptions_And_Expires()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 11, 0));
        var rule = Rule(schedule: new Schedule
        {
            Mode = ScheduleMode.Weekly,
            Windows = [new TimeWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0))]
        });
        var state = new AppState
        {
            Rules = [rule],
            Allowances =
            [
                new Allowance
                {
                    Name = "Ausnahme",
                    Targets = [new Target(TargetKind.Process, "chrome")],
                    Schedule = Schedule.Always()
                }
            ]
        };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.Empty(engine.Snapshot.BlockedProcesses);

        var start = engine.StartLockdown(TimeSpan.FromMinutes(30), "Schreiben", blockBrowsers: false, allowEarlyExit: false);
        Assert.True(start.Ok);
        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.True(engine.Snapshot.LockdownActive);
        Assert.Contains("chrome", engine.Snapshot.BlockedProcesses);
        Assert.False(engine.GrantGrace(rule.Id).Ok);

        clock.Advance(TimeSpan.FromMinutes(31));
        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.False(engine.Snapshot.LockdownActive);
        Assert.Null(state.Lockdown);
    }

    [Fact]
    public void Engine_Creates_No_Block_When_All_Targets_Are_Excepted()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = Rule(limit: 1);
        var allowance = new Allowance
        {
            Targets = [new Target(TargetKind.Process, "chrome")],
            Schedule = Schedule.Always()
        };
        var state = new AppState { Rules = [rule], Allowances = [allowance] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        var snapshot = engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.False(snapshot.FindRule(rule.Id)!.Blocked);
        Assert.Empty(snapshot.BlockingRuleIds);
    }

    [Fact]
    public void Lockdown_Without_Early_Exit_Cannot_Be_Ended_By_Code()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 11, 0));
        var (hash, salt) = ApprovalCodes.Create("ruhig-wald");
        var anna = new Contact
        {
            Name = "Anna",
            IsApprover = true,
            CodeHash = hash,
            CodeSalt = salt
        };
        var state = new AppState { Rules = [Rule()], Contacts = [anna] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        Assert.True(engine.StartLockdown(
            TimeSpan.FromMinutes(30), null, blockBrowsers: false, allowEarlyExit: false).Ok);

        var result = engine.EndLockdownEarly("ruhig-wald");
        Assert.False(result.Ok);
        Assert.Contains("nicht vorzeitig", result.Message);
        Assert.NotNull(state.Lockdown);
    }

    [Fact]
    public void Lockdown_Early_Exit_Requires_The_Approver_Code()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 11, 0));
        var (hash, salt) = ApprovalCodes.Create("ruhig-wald");
        var anna = new Contact
        {
            Name = "Anna",
            IsApprover = true,
            CodeHash = hash,
            CodeSalt = salt
        };
        var state = new AppState { Rules = [Rule()], Contacts = [anna] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        Assert.True(engine.StartLockdown(
            TimeSpan.FromMinutes(30),
            "Schreiben",
            blockBrowsers: false,
            allowEarlyExit: true,
            approverContactId: anna.Id).Ok);

        Assert.False(engine.EndLockdownEarly("falscher-code").Ok);
        Assert.False(engine.EndLockdownEarly(null).Ok);
        Assert.NotNull(state.Lockdown);

        Assert.True(engine.EndLockdownEarly("ruhig-wald").Ok);
        Assert.Null(state.Lockdown);

        engine.Evaluate(ActivitySnapshot.From(["chrome"]));
        Assert.False(engine.Snapshot.LockdownActive);
    }

    [Fact]
    public void Lockdown_Early_Exit_Accepts_Any_Approver_When_None_Is_Pinned()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 11, 0));
        var (annaHash, annaSalt) = ApprovalCodes.Create("anna-code");
        var (jonasHash, jonasSalt) = ApprovalCodes.Create("jonas-code");
        var state = new AppState
        {
            Rules = [Rule()],
            Contacts =
            [
                new Contact
                {
                    Name = "Anna",
                    IsApprover = true,
                    CodeHash = annaHash,
                    CodeSalt = annaSalt
                },
                new Contact
                {
                    Name = "Jonas",
                    IsApprover = true,
                    CodeHash = jonasHash,
                    CodeSalt = jonasSalt
                }
            ]
        };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        Assert.True(engine.StartLockdown(
            TimeSpan.FromMinutes(30), null, blockBrowsers: false, allowEarlyExit: true).Ok);

        Assert.False(engine.EndLockdownEarly("unbekannt").Ok);
        Assert.True(engine.EndLockdownEarly("jonas-code").Ok);
        Assert.Null(state.Lockdown);
    }

    [Fact]
    public void Lockdown_Rejects_A_Contact_Without_A_Code_As_Approver()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 11, 0));
        var contact = new Contact { Name = "Ohne Code", IsApprover = false };
        var state = new AppState { Rules = [Rule()], Contacts = [contact] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        var result = engine.StartLockdown(
            TimeSpan.FromMinutes(30),
            null,
            blockBrowsers: false,
            allowEarlyExit: true,
            approverContactId: contact.Id);

        Assert.False(result.Ok);
        Assert.Null(state.Lockdown);
    }

    [Fact]
    public void Lockdown_Early_Exit_Requires_An_Approver_With_Code()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 11, 0));
        var state = new AppState { Rules = [Rule()] };
        state.Normalize();
        var engine = new FocusEngine(state, clock);

        var result = engine.StartLockdown(
            TimeSpan.FromMinutes(30), null, blockBrowsers: false, allowEarlyExit: true);

        Assert.False(result.Ok);
        Assert.Contains("Freigabecode", result.Message);
        Assert.Null(state.Lockdown);
    }
}
