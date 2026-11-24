using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Still.Core.Models;

namespace Still.App.Services;

/// <summary>
/// Reads the icon embedded in an executable and turns it into an Avalonia bitmap.
/// Extraction uses Win32 only, so no extra package is needed. Pixel data and the
/// finished bitmaps are cached per path; the list is rebuilt on every search.
/// </summary>
public static class AppIconService
{
    private const int IconSize = 32;

    private static readonly ConcurrentDictionary<string, Task<byte[]?>> PixelCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, Bitmap> BitmapCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly SemaphoreSlim Gate = new(4);

    /// <summary>Must be awaited from the UI thread; returns null when no icon can be read.</summary>
    public static async Task<Bitmap?> GetBitmapAsync(string exePath)
    {
        if (BitmapCache.TryGetValue(exePath, out var cached))
        {
            return cached;
        }

        var pixels = await GetPixelsAsync(exePath).ConfigureAwait(true);
        if (pixels is null)
        {
            return null;
        }

        return await Dispatcher.UIThread.InvokeAsync(() =>
            BitmapCache.GetOrAdd(exePath, _ => CreateBitmap(pixels)));
    }

    /// <summary>Finds the installed executable for a process name and reads its icon.</summary>
    public static async Task<Bitmap?> GetForProcessAsync(string processName)
    {
        var normalized = Target.NormalizeProcess(processName);
        if (normalized.Length == 0)
        {
            return null;
        }

        var path = await Task.Run(() => FindExecutable(normalized));
        return string.IsNullOrWhiteSpace(path) ? null : await GetBitmapAsync(path);
    }

    private static string? FindExecutable(string normalized)
    {
        // Prefer a running process: this is immediate, works for portable apps
        // and also finds programs outside the installed-apps folders.
        foreach (var process in Process.GetProcessesByName(normalized))
        {
            try
            {
                using (process)
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception)
            {
                // Access denied is normal for protected and elevated processes.
            }
        }

        if (OperatingSystem.IsWindows() && normalized == "explorer")
        {
            var systemPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            if (File.Exists(systemPath))
            {
                return systemPath;
            }
        }

        return InstalledAppScanner.Scan()
            .FirstOrDefault(app => app.ProcessName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?.Path;
    }

    private static Task<byte[]?> GetPixelsAsync(string exePath) =>
        PixelCache.GetOrAdd(exePath, LoadAsync);

    private static async Task<byte[]?> LoadAsync(string exePath)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Extract(exePath)).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Bitmap CreateBitmap(byte[] pixels)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(IconSize, IconSize),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var buffer = bitmap.Lock();
        for (var y = 0; y < IconSize; y++)
        {
            Marshal.Copy(
                pixels,
                y * IconSize * 4,
                buffer.Address + (y * buffer.RowBytes),
                IconSize * 4);
        }

        return bitmap;
    }

    private static byte[]? Extract(string exePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return null;
        }

        var icons = new IntPtr[1];
        try
        {
            if (ExtractIconEx(exePath, 0, icons, null, 1) == 0 || icons[0] == IntPtr.Zero)
            {
                return null;
            }

            return Render(icons[0]);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (icons[0] != IntPtr.Zero)
            {
                DestroyIcon(icons[0]);
            }
        }
    }

    private static byte[]? Render(IntPtr hIcon)
    {
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = IconSize,
                Height = -IconSize,
                Planes = 1,
                BitCount = 32,
                Compression = 0
            }
        };

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return null;
        }

        IntPtr hBitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            hBitmap = CreateDIBSection(hdc, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            previous = SelectObject(hdc, hBitmap);
            if (!DrawIconEx(hdc, 0, 0, hIcon, IconSize, IconSize, 0, IntPtr.Zero, DiNormal))
            {
                return null;
            }

            var pixels = new byte[IconSize * IconSize * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                SelectObject(hdc, previous);
            }

            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }

            DeleteDC(hdc);
        }
    }

    private const uint DibRgbColors = 0;
    private const uint DiNormal = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? largeIcons, IntPtr[]? smallIcons, uint count);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int width, int height, uint step, IntPtr flickerFreeBrush, uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
