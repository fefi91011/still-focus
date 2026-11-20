using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Still.App.Views;

public partial class BlockedWindow : Window
{
    private readonly DispatcherTimer _focusTimer;
    private bool _allowClose;
    private bool _focusGuardQueued;

    public BlockedWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _focusTimer.Tick += (_, _) =>
        {
            if (!IsVisible)
            {
                return;
            }

            var foreground = OperatingSystem.IsWindows() ? GetForegroundWindow() : IntPtr.Zero;
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero && foreground != handle)
            {
                ForceForeground();
            }
        };

        Opened += async (_, _) =>
        {
            _focusTimer.Start();
            ForceForeground();
            await Task.Delay(80);
            ForceForeground();
            await Task.Delay(180);
            ForceForeground();
        };
        Deactivated += (_, _) => QueueFocusGuard();
        Closed += (_, _) => _focusTimer.Stop();
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
            }
        };
    }

    /// <summary>Lets the host close the overlay during a real application shutdown.</summary>
    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void QueueFocusGuard()
    {
        if (_focusGuardQueued || !IsVisible)
        {
            return;
        }

        _focusGuardQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _focusGuardQueued = false;
            if (IsVisible && !IsActive)
            {
                ForceForeground();
            }
        }, DispatcherPriority.Input);
    }

    private void ForceForeground()
    {
        if (!OperatingSystem.IsWindows())
        {
            Activate();
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        handle = GetAncestor(handle, GetAncestorFlags.Root);
        if (handle == IntPtr.Zero)
        {
            Activate();
            return;
        }

        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            ShowWindow(handle, ShowWindowCommand.Show);
            SetWindowPos(
                handle,
                WindowInsertAfter.Topmost,
                0,
                0,
                0,
                0,
                SetWindowPosFlags.NoMove
                | SetWindowPosFlags.NoSize
                | SetWindowPosFlags.ShowWindow);
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            SetActiveWindow(handle);
            SetFocus(handle);
            SwitchToThisWindow(handle, true);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static class ShowWindowCommand
    {
        public const int Show = 5;
    }

    private enum GetAncestorFlags : uint
    {
        Root = 2
    }

    private static class WindowInsertAfter
    {
        public static readonly IntPtr Topmost = new(-1);
    }

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        NoSize = 0x0001,
        NoMove = 0x0002,
        ShowWindow = 0x0040
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, GetAncestorFlags flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        SetWindowPosFlags flags);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr window, bool altTab);
}
