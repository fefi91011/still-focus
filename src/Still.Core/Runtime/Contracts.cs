namespace Still.Core.Runtime;

public sealed record OperationResult(bool Ok, string Message)
{
    public static OperationResult Success(string message = "") => new(true, message);
    public static OperationResult Failure(string message) => new(false, message);
}

public sealed record ProcessEntry(int ProcessId, string Name, string? WindowTitle);

public interface IProcessController
{
    bool CanBlock { get; }
    string? UnavailableReason { get; }
    IReadOnlyList<ProcessEntry> GetProcesses();
    IReadOnlySet<string> GetForegroundProcesses();
    bool Terminate(int processId);
}

public sealed record WebsiteApplyResult(bool Success, int Entries, string? Error)
{
    public static WebsiteApplyResult Ok(int entries) => new(true, entries, null);
    public static WebsiteApplyResult Failure(string error) => new(false, 0, error);
}

public interface IWebsiteBlocker
{
    bool CanBlock { get; }
    string? UnavailableReason { get; }
    WebsiteApplyResult Apply(IReadOnlyCollection<string> domains);
}

public interface IStartupInstaller
{
    bool CanInstall { get; }
    string? UnavailableReason { get; }
    bool IsInstalled { get; }
    OperationResult Install();
    OperationResult Remove();
}

public interface IPlatform
{
    string DisplayName { get; }
    bool IsElevated { get; }
    bool IsWindows { get; }
    IProcessController Processes { get; }
    IWebsiteBlocker Websites { get; }
    IStartupInstaller Startup { get; }
    void OpenExternal(string target);
    void RevealPath(string path);
}

/// <summary>A short message for the floating blocked window.</summary>
public sealed record BlockNotice(
    string Subject,
    string KindLabel,
    IReadOnlyList<string> RuleNames,
    Guid? GraceRuleId,
    int GraceMinutes,
    int GraceUsesLeft,
    string? ProcessName = null);
