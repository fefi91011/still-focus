using Still.Platform;

namespace Still.App;

/// <summary>
/// Runs as <c>Still.App.exe --cleanup</c> from the uninstaller, elevated, before
/// the files are removed. It clears the hosts section Still manages, even when
/// the app itself was not running, and removes the autostart tasks.
/// The exit code tells the uninstaller what failed: 1 = hosts, 2 = autostart,
/// 4 = unexpected error. Combined failures add up (3 = both, 7 = everything).
/// </summary>
internal static class UninstallCleanup
{
    public const int HostsFailed = 1;
    public const int StartupFailed = 2;
    public const int UnexpectedError = 4;

    public static int Run()
    {
        var code = 0;

        try
        {
            var platform = PlatformFactory.Create();

            if (platform.Websites.CanBlock)
            {
                var result = platform.Websites.Apply(Array.Empty<string>());
                if (!result.Success)
                {
                    code |= HostsFailed;
                    Log("hosts: " + (result.Error ?? "unbekannter Fehler"));
                }
            }

            if (platform.Startup.CanInstall)
            {
                var result = platform.Startup.Remove();
                if (!result.Ok)
                {
                    code |= StartupFailed;
                    Log("autostart: " + result.Message);
                }
            }
        }
        catch (Exception ex)
        {
            code |= UnexpectedError;
            Log(ex.ToString());
        }

        return code;
    }

    private static void Log(string message)
    {
        try
        {
            Console.Error.WriteLine("--cleanup: " + message);
        }
        catch
        {
            // Diagnostics must never change the exit code.
        }
    }
}
