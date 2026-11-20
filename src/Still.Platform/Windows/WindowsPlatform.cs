using System.Diagnostics;
using Still.Core.Runtime;

namespace Still.Platform.Windows;

public sealed class WindowsPlatform : IPlatform
{
    private readonly WindowsProcessController _processes = new();
    private readonly HostsWebsiteBlocker _websites = new();
    private readonly WindowsStartupInstaller _startup = new();

    public string DisplayName => "Windows";

    public bool IsElevated => WindowsElevation.IsElevated;

    public bool IsWindows => true;

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
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
        }
        catch
        {
        }
    }
}
