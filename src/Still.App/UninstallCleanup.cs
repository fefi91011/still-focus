using Still.Platform;

namespace Still.App;

/// <summary>
/// Runs as <c>Still.App.exe --cleanup</c> from the uninstaller, elevated, before
/// the files are removed. It clears the hosts section Still manages, even when
/// the app itself was not running, and removes the autostart tasks.
/// </summary>
internal static class UninstallCleanup
{
    public static int Run()
    {
        try
        {
            var platform = PlatformFactory.Create();
            var ok = true;

            if (platform.Websites.CanBlock)
            {
                var result = platform.Websites.Apply(Array.Empty<string>());
                ok &= result.Success;
            }

            if (platform.Startup.CanInstall)
            {
                var result = platform.Startup.Remove();
                ok &= result.Ok;
            }

            return ok ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
