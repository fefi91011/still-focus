using System.Diagnostics;
using Still.Core.Models;

namespace Still.App.Services;

/// <summary>
/// One executable found on this PC. The process name is what Still stores in a
/// rule; the display name makes the picker readable, and the source explains
/// where the entry came from.
/// </summary>
public sealed record InstalledApplication(
    string Name,
    string ProcessName,
    string Path,
    string Source,
    string? Publisher = null);

/// <summary>
/// Scans the Windows programme folders once on demand: machine-wide installs,
/// 32-bit installs and per-user installs below LocalAppData. The scan is
/// deliberately shallow and ignores inaccessible/system-only locations; it is
/// a picker helper, not a complete software inventory.
/// </summary>
public static class InstalledAppScanner
{
    public const string SourcePrograms = "Programme";
    public const string SourceProgramsX86 = "Programme (x86)";
    public const string SourceUserPrograms = "Benutzerprogramme";

    private static readonly Lazy<IReadOnlyList<InstalledApplication>> Cached = new(
        ScanCore,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<InstalledApplication> Scan() => Cached.Value;

    private static IReadOnlyList<InstalledApplication> ScanCore()
    {
        var roots = new List<(string Path, string Source)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), SourcePrograms),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), SourceProgramsX86),
            (Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs"), SourceUserPrograms)
        };

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 6,
            ReturnSpecialDirectories = false
        };

        var found = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, source) in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.exe", options))
                {
                    if (IsIgnoredPath(path))
                    {
                        continue;
                    }

                    var processName = Target.NormalizeProcess(path);
                    if (processName.Length == 0 || IsNoise(processName) || found.ContainsKey(processName))
                    {
                        continue;
                    }

                    var info = ReadVersionInfo(path);
                    var display = info.Name ?? char.ToUpperInvariant(processName[0]) + processName[1..];
                    found[processName] = new InstalledApplication(display, processName, path, source, info.Publisher);
                }
            }
            catch (Exception)
            {
                // A locked or disappearing directory should not break the picker.
            }
        }

        return found.Values
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(app => app.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsIgnoredPath(string path) =>
        path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Common Files\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Windows Defender\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Installer\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase);

    private static (string? Name, string? Publisher) ReadVersionInfo(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var name = new[] { info.ProductName, info.FileDescription }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value.Trim().Length > 1);
            var publisher = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
            return (name?.Trim(), publisher);
        }
        catch (Exception)
        {
            // Version information is optional.
            return (null, null);
        }
    }

    private static bool IsNoise(string processName) =>
        processName.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
        || processName.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("update", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("setup", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("installer", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("crash", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("redist", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("vc_", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("createdump", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("unitycrashhandler", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("elevation_service", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("notification_helper", StringComparison.OrdinalIgnoreCase)
        || processName is "still" or "still.app" or "still.watchdog";
}
