using System.Diagnostics;

namespace Still.App.Services;

/// <summary>
/// Starts the downloaded setup after Still has closed, so it can replace the
/// running files. The update uses the existing AppId and install directory and
/// runs without wizard pages: no language, directory or task prompts. Windows
/// may still ask once for administrator rights because Still installs into
/// Program Files.
/// </summary>
public static class AppInstaller
{
    public static void StartAfterExit(string installerPath, string? logPath = null, int delaySeconds = 3)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Der Installer wurde nicht gefunden.", installerPath);
        }

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            var logDirectory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
        }

        var logArgument = string.IsNullOrWhiteSpace(logPath)
            ? string.Empty
            : $" /LOG=\"{logPath}\"";

        var arguments =
            $"/c timeout /t {delaySeconds} /nobreak >nul & start \"\" \"{installerPath}\" " +
            "/SILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /CLOSEAPPLICATIONS /SP- " +
            $"/RESTARTSTILL=1{logArgument}";

        Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
