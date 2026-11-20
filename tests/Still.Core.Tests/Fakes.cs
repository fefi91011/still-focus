using Still.Core.Models;
using Still.Core.Persistence;
using Still.Core.Runtime;
using Still.Core.Time;

namespace Still.Core.Tests;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public void Advance(TimeSpan by) => Now += by;
}

internal static class TestTime
{
    public static DateTimeOffset Local(int year, int month, int day, int hour = 0, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}

internal sealed class FakeProcessController : IProcessController
{
    public bool CanBlock { get; set; } = true;
    public string? UnavailableReason { get; set; }
    public List<ProcessEntry> Processes { get; } = new();
    public HashSet<string> Foreground { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<int> Terminated { get; } = new();

    public IReadOnlyList<ProcessEntry> GetProcesses() => Processes;

    public IReadOnlySet<string> GetForegroundProcesses() => Foreground;

    public bool Terminate(int processId)
    {
        Terminated.Add(processId);
        return true;
    }
}

internal sealed class FakeWebsiteBlocker : IWebsiteBlocker
{
    public bool CanBlock { get; set; } = true;
    public string? UnavailableReason { get; set; }
    public List<HashSet<string>> Calls { get; } = new();

    public IReadOnlyCollection<string> Last =>
        Calls.Count == 0 ? Array.Empty<string>() : Calls[^1];

    public WebsiteApplyResult Apply(IReadOnlyCollection<string> domains)
    {
        Calls.Add(new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase));
        return WebsiteApplyResult.Ok(domains.Count);
    }
}

internal sealed class FakeStartupInstaller : IStartupInstaller
{
    public bool CanInstall { get; set; } = true;
    public string? UnavailableReason { get; set; }
    public bool IsInstalled { get; set; }
    public OperationResult Install() => OperationResult.Success();
    public OperationResult Remove() => OperationResult.Success();
}

internal sealed class FakePlatform : IPlatform
{
    public FakePlatform()
    {
        Processes = new FakeProcessController();
        Websites = new FakeWebsiteBlocker();
    }

    public string DisplayName => "Test";

    public bool IsElevated => true;

    public bool IsWindows => true;

    public FakeProcessController Processes { get; }

    IProcessController IPlatform.Processes => Processes;

    public FakeWebsiteBlocker Websites { get; }

    IWebsiteBlocker IPlatform.Websites => Websites;

    public IStartupInstaller Startup { get; } = new FakeStartupInstaller();

    public List<string> Opened { get; } = new();

    public void OpenExternal(string target) => Opened.Add(target);

    public void RevealPath(string path) => Opened.Add(path);
}

internal sealed class MemoryStore : IStateStore
{
    public MemoryStore(string directory)
    {
        DataDirectory = directory;
        StateFilePath = Path.Combine(directory, "state.json");
    }

    public string DataDirectory { get; }

    public string StateFilePath { get; }

    public AppState Saved { get; private set; } = new();

    public int Saves { get; private set; }

    public int Heartbeats { get; private set; }

    public AppState Load(out string? warning)
    {
        warning = null;
        return Saved;
    }

    public void Save(AppState state)
    {
        Saved = state;
        Saves++;
    }

    public void WriteHeartbeat(DateTimeOffset now) => Heartbeats++;

    public void ClearHeartbeat()
    {
    }
}
