using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Still.App.Services;
using Still.App.ViewModels;
using Still.App.Views;
using Still.Core.Runtime;

namespace Still.Screenshots;

internal static class Program
{
    private static readonly (string Route, string Name)[] Pages =
    [
        ("today", "heute"),
        ("focus", "fokus"),
        ("tasks", "aufgaben"),
        ("contacts", "kontakte"),
        ("rules", "regeln"),
        ("schedule", "zeitplan"),
        ("stats", "statistik"),
        ("settings", "einstellungen")
    ];

    private static string _output = string.Empty;
    private static int _index = 1;

    [STAThread]
    public static int Main(string[] args)
    {
        _output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath("artifacts/screenshots");
        Directory.CreateDirectory(_output);

        AppBuilder.Configure<Still.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter" })
            .SetupWithoutStarting();

        var runtime = DemoData.CreateRuntime();
        var viewModel = new MainViewModel(runtime, demoMode: true);
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1320,
            Height = 860
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (var (route, name) in Pages)
        {
            viewModel.Navigate(route);
            viewModel.Refresh();
            viewModel.ApplySnapshot(runtime.Snapshot);
            Capture(window, name);
        }

        viewModel.Navigate("rules");
        viewModel.Rules.NewRuleCommand.Execute(null);
        Capture(window, "regel-editor");

        viewModel.Rules.Editor.ScheduleEnabled = true;
        Dispatcher.UIThread.RunJobs();
        Capture(window, "regel-editor-woche");

        viewModel.Rules.Editor.CancelCommand.Execute(null);
        viewModel.Rules.ShowAllowancesTabCommand.Execute(null);
        Capture(window, "ausnahmen");

        viewModel.Rules.NewAllowanceCommand.Execute(null);
        Capture(window, "ausnahme-editor");
        viewModel.Rules.AllowanceEditor.CancelCommand.Execute(null);

        viewModel.Navigate("tasks");
        viewModel.Tasks.NewTaskCommand.Execute(null);
        Capture(window, "aufgaben-editor");

        viewModel.Tasks.Editor.CancelCommand.Execute(null);
        viewModel.Navigate("contacts");
        viewModel.Contacts.NewContactCommand.Execute(null);
        Capture(window, "kontakt-editor");

        viewModel.Contacts.Editor.CancelCommand.Execute(null);

        var approver = runtime.State.Contacts.First(c => c.IsApprover && c.HasCode);
        var lockdown = runtime.StartLockdown(
            TimeSpan.FromMinutes(60),
            "Artikel fertig schreiben",
            blockBrowsers: true,
            allowEarlyExit: true,
            approverContactId: approver.Id);
        if (!lockdown.Ok)
        {
            throw new InvalidOperationException(lockdown.Message);
        }

        viewModel.Navigate("focus");
        viewModel.ApplySnapshot(runtime.Snapshot);
        Capture(window, "cold-turkey");

        var release = runtime.EndLockdownEarly("ruhig-wald");
        if (!release.Ok)
        {
            throw new InvalidOperationException(release.Message);
        }

        viewModel.Navigate("today");
        _ = viewModel.PromptAsync(
            "Freigabe nötig",
            "Für diese Änderung brauchst du den Code einer Freigabeperson.",
            "Freigabecode",
            password: true,
            placeholder: "Code der Kontaktperson",
            confirmText: "Freigeben");
        Dispatcher.UIThread.RunJobs();
        Capture(window, "dialog");
        viewModel.Dialog.CancelCommand.Execute(null);

        viewModel.ShowToast("Einstellungen gespeichert.");
        Dispatcher.UIThread.RunJobs();
        Capture(window, "toast");

        var blocked = new BlockedWindow
        {
            Width = 1320,
            Height = 860,
            DataContext = new BlockedWindowViewModel(
                new BlockNotice("YouTube", "Website gesperrt", ["Deep Work"], Guid.NewGuid(), 5, 2),
                close: () => { },
                grantGrace: _ => { },
                contact: () => { })
        };
        blocked.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(blocked, "blockiert");
        blocked.Close();

        window.Close();
        return 0;
    }

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var width = window.ClientSize.Width > 0 ? (int)window.ClientSize.Width : (int)window.Width;
        var height = window.ClientSize.Height > 0 ? (int)window.ClientSize.Height : (int)window.Height;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(window);
        var path = Path.Combine(_output, $"{_index:00}-{name}.png");
        bitmap.Save(path);
        Console.WriteLine($"{name}: {width}x{height} -> {path}");
        _index++;
    }
}
