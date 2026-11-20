using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Still.App.ViewModels;
using Still.App.Views;
using Still.Core.Engine;
using Still.Core.Models;
using Still.Core.Persistence;
using Still.Core.Runtime;
using Still.Platform;

namespace Still.App.Services;

/// <summary>
/// Owns the window, tray icon and per-second evaluation loop. Kept out of the
/// view models so the UI remains testable with headless rendering.
/// </summary>
public sealed class AppHost
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly StillRuntime _runtime;
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _timer;
    private TrayIcon? _tray;
    private BlockedWindow? _blockedWindow;
    private ExtensionBridge? _extensionBridge;
    private bool _shutdown;

    private AppHost(
        IClassicDesktopStyleApplicationLifetime lifetime,
        StillRuntime runtime,
        MainViewModel viewModel,
        bool demo)
    {
        _lifetime = lifetime;
        _runtime = runtime;
        _viewModel = viewModel;
        IsDemo = demo;

        Window = new MainWindow
        {
            DataContext = viewModel
        };

        Window.Closing += OnClosing;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
    }

    public MainWindow Window { get; }
    public bool IsDemo { get; }

    public static AppHost Create(IClassicDesktopStyleApplicationLifetime lifetime, bool demo)
    {
        StillRuntime runtime;
        if (demo)
        {
            runtime = DemoData.CreateRuntime();
        }
        else
        {
            var store = new JsonStateStore();
            var state = store.Load(out var warning);
            runtime = new StillRuntime(state, store, PlatformFactory.Create(), null, warning);
            runtime.Initialize();
        }

        var viewModel = new MainViewModel(runtime, demo);
        return new AppHost(lifetime, runtime, viewModel, demo);
    }

    public void Start()
    {
        _viewModel.ApplySnapshot(_runtime.Snapshot);
        _viewModel.Refresh();

        _runtime.SnapshotChanged += OnSnapshot;
        _runtime.BlockDetected += OnBlockDetected;

        CreateTrayIcon();

        if (!IsDemo)
        {
            try
            {
                _extensionBridge = new ExtensionBridge(_runtime, OpenFromExtension, ContactFromExtension);
                _extensionBridge.Start();
            }
            catch (Exception ex)
            {
                _extensionBridge = null;
                Log(ex);
            }
        }

        if (IsDemo || !_runtime.State.Settings.StartMinimized)
        {
            ShowWindow();
        }

        _timer.Start();
    }

    public void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _timer.Stop();

        _runtime.SnapshotChanged -= OnSnapshot;
        _runtime.BlockDetected -= OnBlockDetected;

        _extensionBridge?.Dispose();
        _extensionBridge = null;

        try
        {
            _blockedWindow?.CloseForShutdown();
        }
        catch
        {
        }

        if (_tray is not null)
        {
            _tray.IsVisible = false;
            _tray = null;
        }

        try
        {
            _runtime.Dispose();
        }
        catch (Exception ex)
        {
            Log(ex);
        }

        Window.Close();
        _lifetime.Shutdown();
    }

    private void OnTick()
    {
        try
        {
            _runtime.Tick();
            _viewModel.Refresh();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void OnSnapshot(EngineSnapshot snapshot)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _viewModel.ApplySnapshot(snapshot);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _viewModel.ApplySnapshot(snapshot));
        }
    }

    private void OnBlockDetected(BlockNotice notice)
    {
        if (!_runtime.State.Settings.ShowBlockedOverlay)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => ShowBlocked(notice));
    }

    private void ShowBlocked(BlockNotice notice)
    {
        if (_shutdown)
        {
            return;
        }

        _blockedWindow ??= new BlockedWindow();
        _blockedWindow.DataContext = new BlockedWindowViewModel(
            notice,
            close: () => CloseBlockedApp(notice.ProcessName),
            grantGrace: ruleId =>
            {
                // Apply the grace before hiding. Otherwise a queued UI tick can
                // still see the old blocked snapshot and show the overlay again.
                _viewModel.GrantGrace(ruleId);
                HideBlocked();
            },
            contact: () =>
            {
                HideBlocked();
                _runtime.ResetBlockNotice(notice.ProcessName);
                ShowWindow();
                _viewModel.ContactFromBlock();
            });

        if (!_blockedWindow.IsVisible)
        {
            _blockedWindow.Show();
        }

        _blockedWindow.Activate();
    }

    private void HideBlocked() => _blockedWindow?.Hide();

    private void CloseBlockedApp(string? processName)
    {
        var result = _runtime.CloseBlockedProcess(processName);
        if (!result.Ok)
        {
            _viewModel.ShowToast(result.Message, error: true);
            return;
        }

        HideBlocked();
        _runtime.ResetBlockNotice(processName);
    }

    private void ContactFromExtension()
    {
        Dispatcher.UIThread.Post(() =>
        {
            HideBlocked();
            _runtime.ResetBlockNotice();
            ShowWindow();
            _viewModel.ContactFromBlock();
        });
    }

    private void CreateTrayIcon()
    {
        try
        {
            var menu = new NativeMenu();

            var open = new NativeMenuItem { Header = "Still öffnen" };
            open.Click += (_, _) => ShowWindow();
            menu.Items.Add(open);

            var focus = new NativeMenuItem { Header = "25-Minuten-Fokus" };
            focus.Click += (_, _) =>
            {
                ShowWindow();
                _viewModel.Navigate("focus");
                var result = _runtime.StartPomodoro(new PomodoroStartOptions(25, 5, 15, 4));
                _viewModel.Notify(result);
            };
            menu.Items.Add(focus);

            var locked = new NativeMenuItem { Header = "Cold Turkey" };
            locked.Click += (_, _) =>
            {
                ShowWindow();
                _viewModel.Navigate("focus");
                _viewModel.ShowToast("Wähle unten Dauer und Bestätigung für Cold Turkey.");
            };
            menu.Items.Add(locked);

            menu.Items.Add(new NativeMenuItemSeparator());

            var exit = new NativeMenuItem { Header = "Beenden" };
            exit.Click += (_, _) => RequestExit();
            menu.Items.Add(exit);

            _tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Still.App/Assets/still.ico"))),
                ToolTipText = "Still · Fokus, der bleibt",
                Menu = menu,
                IsVisible = true
            };
            _tray.Clicked += (_, _) => ShowWindow();
        }
        catch (Exception ex)
        {
            Log(ex);
            _tray = null;
        }
    }

    private void ShowWindow()
    {
        if (!Window.IsVisible)
        {
            Window.Show();
        }

        if (Window.WindowState == WindowState.Minimized)
        {
            Window.WindowState = WindowState.Normal;
        }

        Window.Activate();
    }

    private void OpenFromExtension()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ShowWindow();
            _viewModel.Navigate("focus");
        });
    }

    private void RequestExit()
    {
        if (_runtime.IsLockdownActive)
        {
            ShowWindow();
            _viewModel.Navigate("focus");
            _viewModel.ShowToast("Cold Turkey läuft. Beenden ist erst danach möglich.", error: true);
            return;
        }

        Shutdown();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdown)
        {
            return;
        }

        if (_runtime.State.Settings.CloseToTray || _runtime.IsLockdownActive)
        {
            e.Cancel = true;
            Window.Hide();
            return;
        }

        Shutdown();
    }

    private void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(_runtime.DataDirectory);
            File.AppendAllText(
                Path.Combine(_runtime.DataDirectory, "still.log"),
                $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
