using System.Diagnostics;
using Still.Core.Runtime;

namespace Still.Platform.Null;

/// <summary>No-op platform used on Linux/macOS and for UI previews.</summary>
public sealed class NullPlatform : IPlatform
{
    private readonly NullProcessController _processes = new();
    private readonly NullWebsiteBlocker _websites = new();
    private readonly NullStartupInstaller _startup = new();

    public string DisplayName => "Demo";

    public bool IsElevated => false;

    public bool IsWindows => OperatingSystem.IsWindows();

    public IProcessController Processes => _processes;

    public IWebsiteBlocker Websites => _websites;

    public IStartupInstaller Startup => _startup;

    public void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public void RevealPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}

internal sealed class NullProcessController : IProcessController
{
    public bool CanBlock => false;

    public string? UnavailableReason => "Programm-Sperren sind in der Demo nicht aktiv.";

    public IReadOnlyList<ProcessEntry> GetProcesses() => Array.Empty<ProcessEntry>();

    public IReadOnlySet<string> GetForegroundProcesses() =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool Terminate(int processId) => false;
}

internal sealed class NullWebsiteBlocker : IWebsiteBlocker
{
    public bool CanBlock => false;

    public string? UnavailableReason => "Website-Sperren sind in der Demo nicht aktiv.";

    public WebsiteApplyResult Apply(IReadOnlyCollection<string> domains) =>
        WebsiteApplyResult.Failure("Website-Sperren sind in der Demo nicht aktiv.");
}

internal sealed class NullStartupInstaller : IStartupInstaller
{
    public bool CanInstall => false;

    public string? UnavailableReason => "Autostart ist nur unter Windows verfügbar.";

    public bool IsInstalled => false;

    public OperationResult Install() => OperationResult.Failure(UnavailableReason!);

    public OperationResult Remove() => OperationResult.Failure(UnavailableReason!);
}
