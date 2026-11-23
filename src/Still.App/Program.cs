using Avalonia;
using Avalonia.Media;

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
