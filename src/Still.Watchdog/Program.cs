using System.Diagnostics;
using Still.Core.Persistence;

namespace Still.Watchdog;

/// <summary>
/// Tiny scheduled-task companion. It never touches the state itself; it only
/// restarts Still when the heartbeat is missing or stale, including while a
/// Cold Turkey lock is active.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private static int Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Log("Watchdog is a Windows-only component; nothing to do.");
                return 0;
            }

            var store = new JsonStateStore();
            var heartbeat = ReadHeartbeat(store.HeartbeatFilePath);
            if (heartbeat is not null && DateTimeOffset.Now - heartbeat < StaleAfter)
            {
                return 0;
            }

            var state = store.Load(out _);
            var lockdown = state.Lockdown?.IsActiveAt(DateTimeOffset.Now) == true;

            var app = Path.Combine(AppContext.BaseDirectory, "Still.App.exe");
            if (!File.Exists(app))
            {
                Log("Still.App.exe was not found next to the watchdog.");
                return 1;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = app,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });

            Log(lockdown
                ? "Still was not running during Cold Turkey; app restarted."
                : "Still was not running; app restarted.");
            return 0;
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            return 1;
        }
    }

    private static DateTimeOffset? ReadHeartbeat(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return DateTimeOffset.TryParse(File.ReadAllText(path).Trim(), out var value)
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Log(string message)
    {
        try
        {
            var directory = new JsonStateStore().DataDirectory;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "watchdog.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // A watchdog must never crash because logging failed.
        }
    }
}
