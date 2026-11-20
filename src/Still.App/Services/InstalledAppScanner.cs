using System.Diagnostics;
using Still.Core.Models;

namespace Still.App.Services;

/// <summary>
/// One executable found below the Windows programme folders. The process name is
/// what Still stores in a rule; the display name makes the picker readable.
/// </summary>
public sealed record InstalledApplication(string Name, string ProcessName, string Path);

/// <summary>
/// Scans the programme folders once on demand. The scan is deliberately shallow
/// and ignores inaccessible/system-only locations; it is a picker helper, not a
/// complete software inventory.
/// </summary>
public static class InstalledAppScanner
{
    private static readonly Lazy<IReadOnlyList<InstalledApplication>> Cached = new(
        ScanCore,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<InstalledApplication> Scan() => Cached.Value;

    private static IReadOnlyList<InstalledApplication> ScanCore()
    {
        var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432")
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 4,
            ReturnSpecialDirectories = false
        };

        var found = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.exe", options))
                {
                    var processName = Target.NormalizeProcess(path);
                    if (processName.Length == 0 || IsNoise(processName) || found.ContainsKey(processName))
                    {
                        continue;
                    }

                    var display = ReadDisplayName(path, processName);
                    found[processName] = new InstalledApplication(display, processName, path);
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

    private static string ReadDisplayName(string path, string processName)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var name = new[] { info.ProductName, info.FileDescription }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }
        catch (Exception)
        {
            // Version information is optional.
        }

        return char.ToUpperInvariant(processName[0]) + processName[1..];
    }

    private static bool IsNoise(string processName) =>
        processName.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
        || processName.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("update", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("setup", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("installer", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("crash", StringComparison.OrdinalIgnoreCase)
        || processName is "still" or "still.app" or "still.watchdog";
}
