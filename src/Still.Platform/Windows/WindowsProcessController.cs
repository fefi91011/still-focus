using System.Diagnostics;
using System.Runtime.InteropServices;
using Still.Core.Models;
using Still.Core.Runtime;

namespace Still.Platform.Windows;

public sealed class WindowsProcessController : IProcessController
{
    public bool CanBlock => OperatingSystem.IsWindows();

    public string? UnavailableReason => CanBlock
        ? null
        : "Programm-Sperren sind nur unter Windows verfügbar.";

    public IReadOnlyList<ProcessEntry> GetProcesses()
    {
        var result = new List<ProcessEntry>();
        if (!CanBlock)
        {
            return result;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                result.Add(new ProcessEntry(process.Id, process.ProcessName, null));
            }
            catch
            {
                // Access denied for some system processes; they are never block targets.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    public IReadOnlySet<string> GetForegroundProcesses()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!CanBlock)
        {
            return result;
        }

        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return result;
        }

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return result;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var name = Target.NormalizeProcess(process.ProcessName);
            if (name.Length > 0)
            {
                result.Add(name);
            }
        }
        catch
        {
            // Process vanished between the two calls.
        }

        return result;
    }

    public bool Terminate(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }

            try
            {
                if (process.CloseMainWindow())
                {
                    process.WaitForExit(600);
                }
            }
            catch
            {
                // No main window or closed already.
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
