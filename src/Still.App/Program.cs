using Avalonia;
using Avalonia.Media;
using Still.Platform.Windows;

namespace Still.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Called elevated by the uninstaller before the files are removed.
        if (args.Any(arg => arg.Equals("--cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            return UninstallCleanup.Run();
        }

        // Called once by the installer. Older versions wrote a hosts section;
        // website blocking now lives in the browser extension.
        if (args.Any(arg => arg.Equals("--cleanup-hosts", StringComparison.OrdinalIgnoreCase)))
        {
            return LegacyHostsCleanup.Run();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter"
            })
            .LogToTrace();
}
