using Still.Core.Models;
using Still.Core.Runtime;
using Xunit;

namespace Still.Core.Tests;

public sealed class RuntimeTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "still-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Runtime_Raises_A_Notice_Without_Terminating_Until_Explicitly_Closed()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = new BlockRule
        {
            Name = "Chrome",
            Targets = [new Target(TargetKind.Process, "chrome")],
            Schedule = Schedule.Always(),
            DailyLimitMinutes = 1,
            GraceMinutes = 5,
            MaxGraceUsesPerDay = 1
        };
        var state = new AppState { Rules = [rule] };
        state.Normalize();

        var platform = new FakePlatform();
        platform.Processes.Processes.Add(new ProcessEntry(4242, "chrome", null));
        platform.Processes.Foreground.Add("chrome");

        var store = new MemoryStore(TempDir());
        using var runtime = new StillRuntime(state, store, platform, clock);
        BlockNotice? notice = null;
        runtime.BlockDetected += n => notice = n;

        runtime.Initialize();
        Assert.Empty(platform.Processes.Terminated);

        for (var i = 0; i < 60; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            runtime.Tick();
        }

        Assert.Empty(platform.Processes.Terminated);
        Assert.NotNull(notice);
        Assert.Equal("Chrome", notice!.Subject);
        Assert.Equal("chrome", notice.ProcessName);
        Assert.Equal(rule.Id, notice.GraceRuleId);
        Assert.True(store.Heartbeats > 0);

        var closed = runtime.CloseBlockedProcess("chrome");
        Assert.True(closed.Ok, closed.Message);
        Assert.Contains(4242, platform.Processes.Terminated);
    }

    [Fact]
    public void Blocked_Overlay_Notice_Reappears_After_App_Leaves_Foreground()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = new BlockRule
        {
            Name = "Chrome",
            Targets = [new Target(TargetKind.Process, "chrome")],
            Schedule = Schedule.Always()
        };
        var state = new AppState { Rules = [rule] };
        state.Normalize();

        var platform = new FakePlatform();
        platform.Processes.Foreground.Add("chrome");
        using var runtime = new StillRuntime(
            state, new MemoryStore(TempDir()), platform, clock);
        var notices = 0;
        runtime.BlockDetected += _ => notices++;

        runtime.Initialize();
        clock.Advance(TimeSpan.FromSeconds(1));
        runtime.Tick();
        Assert.Equal(1, notices);

        // Leaving the app must clear its throttle so returning shows the overlay again.
        platform.Processes.Foreground.Clear();
        clock.Advance(TimeSpan.FromSeconds(1));
        runtime.Tick();
        platform.Processes.Foreground.Add("chrome");
        clock.Advance(TimeSpan.FromSeconds(1));
        runtime.Tick();

        Assert.Equal(2, notices);
    }

    [Fact]
    public void Runtime_Applies_And_Clears_Hosts_Entries()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = new BlockRule
        {
            Name = "YouTube",
            Targets = [new Target(TargetKind.Website, "youtube.com")],
            Schedule = Schedule.Always()
        };
        var state = new AppState { Rules = [rule] };
        state.Normalize();

        var platform = new FakePlatform();
        var store = new MemoryStore(TempDir());
        using var runtime = new StillRuntime(state, store, platform, clock);

        runtime.Initialize();
        var first = platform.Websites.Last;
        Assert.Contains("youtube.com", first);
        Assert.Contains("dns.google", first);

        var disabled = runtime.SetRuleEnabled(rule.Id, false, approved: true);
        Assert.True(disabled.Ok);
        runtime.Tick();
        Assert.Empty(platform.Websites.Last);
    }

    [Fact]
    public void Website_Status_Matches_Subdomains_And_Reports_Grace()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = new BlockRule
        {
            Name = "Deep Work",
            Targets = [new Target(TargetKind.Website, "youtube.com", "YouTube")],
            Schedule = Schedule.Always(),
            DailyLimitMinutes = 1,
            GraceMinutes = 5,
            MaxGraceUsesPerDay = 2
        };
        var state = new AppState { Rules = [rule] };
        state.Normalize();

        var platform = new FakePlatform();
        platform.Processes.Foreground.Add("chrome");
        using var runtime = new StillRuntime(state, new MemoryStore(TempDir()), platform, clock);
        runtime.Initialize();

        for (var i = 0; i < 60; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            runtime.Tick();
        }

        var status = runtime.GetWebsiteStatus("m.youtube.com");
        Assert.True(status.Blocked);
        Assert.Equal("m.youtube.com", status.Host);
        Assert.Equal("YouTube", status.Subject);
        Assert.Contains("Deep Work", status.RuleNames);
        Assert.Equal(rule.Id, status.GraceRuleId);
        Assert.Equal(5, status.GraceMinutes);
        Assert.Equal(2, status.GraceUsesLeft);
        Assert.True(status.GraceAvailable);

        var granted = runtime.GrantWebsiteGrace("m.youtube.com", rule.Id);
        Assert.True(granted.Ok, granted.Message);
        Assert.False(runtime.GetWebsiteStatus("m.youtube.com").Blocked);
        Assert.False(runtime.GrantWebsiteGrace("example.com", rule.Id).Ok);
    }

    [Fact]
    public void Website_Status_Without_A_Limit_Has_No_Grace_Button()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var rule = new BlockRule
        {
            Name = "Abend",
            Targets = [new Target(TargetKind.Website, "reddit.com", "Reddit")],
            Schedule = Schedule.Always()
        };
        var state = new AppState { Rules = [rule] };
        state.Normalize();

        using var runtime = new StillRuntime(
            state, new MemoryStore(TempDir()), new FakePlatform(), clock);
        runtime.Initialize();

        var status = runtime.GetWebsiteStatus("old.reddit.com");
        Assert.True(status.Blocked);
        Assert.Equal("Reddit", status.Subject);
        Assert.Null(status.GraceRuleId);
        Assert.False(status.GraceAvailable);
        Assert.False(runtime.GrantWebsiteGrace("old.reddit.com", rule.Id).Ok);
    }

    [Fact]
    public void Approval_Protects_Edits_When_Enabled()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var contact = new Contact { Name = "Anna", IsApprover = true };
        var (hash, salt) = ApprovalCodes.Create("ruhig-wald");
        contact.CodeHash = hash;
        contact.CodeSalt = salt;

        var state = new AppState { Contacts = [contact] };
        state.Settings.RequireApprovalForEdits = true;
        state.Normalize();

        var platform = new FakePlatform();
        var store = new MemoryStore(TempDir());
        using var runtime = new StillRuntime(state, store, platform, clock);

        Assert.True(runtime.RequiresApprovalForEdits);
        Assert.False(runtime.ValidateApproval("falscher-code"));
        Assert.True(runtime.ValidateApproval("ruhig-wald"));

        var draft = new BlockRule
        {
            Name = "Neu",
            Targets = [new Target(TargetKind.Process, "firefox")],
            Schedule = Schedule.Always()
        };

        Assert.False(runtime.SaveRule(draft, approved: false).Ok);
        Assert.True(runtime.SaveRule(draft, approved: true).Ok);
    }

    [Fact]
    public void Pinned_Approver_Is_Protected_During_Cold_Turkey()
    {
        var clock = new FakeClock(TestTime.Local(2026, 3, 2, 10, 0));
        var (hash, salt) = ApprovalCodes.Create("ruhig-wald");
        var anna = new Contact
        {
            Name = "Anna",
            IsApprover = true,
            CodeHash = hash,
            CodeSalt = salt
        };
        var state = new AppState { Contacts = [anna] };
        state.Normalize();

        using var runtime = new StillRuntime(state, new MemoryStore(TempDir()), new FakePlatform(), clock);
        runtime.Initialize();

        Assert.True(runtime.StartLockdown(
            TimeSpan.FromMinutes(30),
            "Schreiben",
            blockBrowsers: true,
            allowEarlyExit: true,
            approverContactId: anna.Id).Ok);

        var edited = anna.Clone();
        edited.Name = "Anna neu";
        Assert.False(runtime.SaveContact(edited, null, approved: true).Ok);
        Assert.False(runtime.DeleteContact(anna.Id, approved: true).Ok);

        Assert.True(runtime.EndLockdownEarly("ruhig-wald").Ok);
        Assert.True(runtime.DeleteContact(anna.Id, approved: true).Ok);
    }
}
