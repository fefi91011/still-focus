using System.Diagnostics;
using Still.Core.Runtime;

namespace Still.Platform.Windows;

public sealed class WindowsStartupInstaller : IStartupInstaller
{
    private const string AppTask = "Still Autostart";
    private const string WatchdogTask = "Still Watchdog";

    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private bool _lastInstalled;

    public bool CanInstall =>
        OperatingSystem.IsWindows()
        && WindowsElevation.IsElevated
        && FindWatchdog() is not null
        && IsInstalledApplication();

    public string? UnavailableReason
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return "Autostart ist nur unter Windows verfügbar.";
            }

            if (!WindowsElevation.IsElevated)
            {
                return "Der Autostart benötigt Administratorrechte.";
            }

            if (!IsInstalledApplication())
            {
                return "Autostart lässt sich nur aus der veröffentlichten App einrichten.";
            }

            if (FindWatchdog() is null)
            {
                return "Still.Watchdog.exe wurde nicht neben der App gefunden.";
            }

            return null;
        }
    }

    public bool IsInstalled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            if (DateTimeOffset.Now - _lastCheck < TimeSpan.FromSeconds(20))
            {
                return _lastInstalled;
            }

            _lastInstalled = QueryTask(AppTask);
            _lastCheck = DateTimeOffset.Now;
            return _lastInstalled;
        }
    }

    public OperationResult Install()
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Failure("Autostart ist nur unter Windows verfügbar.");
        }

        if (!WindowsElevation.IsElevated)
        {
            return OperationResult.Failure(
                "Bitte starte Still als Administrator, um den geschützten Autostart einzurichten.");
        }

        var app = Environment.ProcessPath;
        if (app is null || !IsInstalledApplication())
        {
            return OperationResult.Failure(
                "Autostart lässt sich nur aus der veröffentlichten App einrichten.");
        }

        var watchdog = FindWatchdog();
        if (watchdog is null)
        {
            return OperationResult.Failure(
                "Still.Watchdog.exe wurde nicht neben der App gefunden.");
        }

        var appResult = RunTool(
            "schtasks.exe",
            "/Create", "/F",
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/TN", AppTask,
            "/TR", $"\"{app}\"");

        if (!appResult.Ok)
        {
            return OperationResult.Failure("Autostart konnte nicht eingerichtet werden: " + appResult.Output);
        }

        var watchdogResult = RunTool(
            "schtasks.exe",
            "/Create", "/F",
            "/SC", "MINUTE",
            "/MO", "1",
            "/RL", "HIGHEST",
            "/TN", WatchdogTask,
            "/TR", $"\"{watchdog}\" --once");

        if (!watchdogResult.Ok)
        {
            return OperationResult.Failure(
                "Der Watchdog konnte nicht eingerichtet werden: " + watchdogResult.Output);
        }

        _lastCheck = DateTimeOffset.MinValue;
        return OperationResult.Success(
            "Geschützter Autostart und Watchdog sind eingerichtet (Start bei Anmeldung).");
    }

    public OperationResult Remove()
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Failure("Autostart ist nur unter Windows verfügbar.");
        }

        if (!WindowsElevation.IsElevated)
        {
            return OperationResult.Failure("Zum Entfernen werden Administratorrechte benötigt.");
        }

        RunTool("schtasks.exe", "/Delete", "/F", "/TN", AppTask);
        RunTool("schtasks.exe", "/Delete", "/F", "/TN", WatchdogTask);
        _lastCheck = DateTimeOffset.MinValue;
        return OperationResult.Success("Autostart und Watchdog wurden entfernt.");
    }

    private static bool QueryTask(string name)
    {
        var result = RunTool("schtasks.exe", "/Query", "/TN", name);
        return result.Ok;
    }

    private static bool IsInstalledApplication()
    {
        var path = Environment.ProcessPath;
        return path is not null
               && Path.GetFileName(path).Equals("Still.App.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindWatchdog()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "Still.Watchdog.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static (bool Ok, string Output) RunTool(string file, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (false, "Prozess konnte nicht gestartet werden.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(8000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                }

                return (false, "Zeitüberschreitung.");
            }

            var text = (output + Environment.NewLine + error).Trim();
            return (process.ExitCode == 0, text);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
