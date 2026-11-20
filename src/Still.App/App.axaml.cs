using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Still.App.Services;

namespace Still.App;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private AppHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var demo = (desktop.Args ?? Array.Empty<string>())
                .Any(arg => arg.Equals("--demo", StringComparison.OrdinalIgnoreCase));

            if (!demo)
            {
                _singleInstance = new Mutex(initiallyOwned: true, "Still.SingleInstance", out var createdNew);
                if (!createdNew)
                {
                    ShowAlreadyRunning(desktop);
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
            }

            _host = AppHost.Create(desktop, demo);
            desktop.MainWindow = _host.Window;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => _host?.Shutdown();
            _host.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowAlreadyRunning(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var message = new Window
        {
            Title = "Still läuft bereits",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brush.Parse("#0A0C0F"),
            Content = new Border
            {
                Padding = new Thickness(26),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Still läuft bereits",
                            FontSize = 20,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brush.Parse("#F1F4F7")
                        },
                        new TextBlock
                        {
                            Text = "Die App ist schon aktiv. Öffne sie über das Symbol im Infobereich der Taskleiste.",
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 12.5,
                            Foreground = Brush.Parse("#99A3B2")
                        }
                    }
                }
            }
        };

        desktop.MainWindow = message;
        message.Show();
    }
}
