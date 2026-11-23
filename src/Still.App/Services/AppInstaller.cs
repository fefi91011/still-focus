using System.Diagnostics;

namespace Still.App.Services;

/// <summary>
/// Starts the downloaded setup after Still has closed, so the installer can
/// replace the running files. The wizard stays visible; nothing is silent.
/// </summary>
public static class AppInstaller
{
    public static void StartAfterExit(string installerPath, int delaySeconds = 3)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Der Installer wurde nicht gefunden.", installerPath);
        }

        var arguments =
            $"/c timeout /t {delaySeconds} /nobreak >nul & start \"\" \"{installerPath}\" /NORESTART";

        Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
