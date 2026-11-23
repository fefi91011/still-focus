using System.Diagnostics;

namespace Still.App.Services;

/// <summary>
/// The Inno Setup uninstaller sits next to the app. It is started after Still
/// has closed, so no file is locked while it removes the installation.
/// </summary>
public static class AppUninstaller
{
    private const string UninstallerName = "unins000.exe";

    public static string? Find()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, UninstallerName);
        return File.Exists(candidate) ? candidate : null;
    }

    public static void StartAfterExit(string uninstallerPath, int delaySeconds = 3)
    {
        var arguments =
            $"/c timeout /t {delaySeconds} /nobreak >nul & \"{uninstallerPath}\" /SILENT /SUPPRESSMSGBOXES /NORESTART";

        Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
