using System.Runtime.InteropServices;
using System.Text;

namespace Still.Platform.Windows;

/// <summary>
/// Removes the hosts-file section written by Still 0.1.x and earlier. Website
/// blocking moved to the browser extension, so this code exists only for
/// upgrades and uninstall cleanup. User entries outside the section are kept.
/// </summary>
public static class LegacyHostsCleanup
{
    private const string BeginMarker = "# >>> Still block begin >>>";
    private const string EndMarker = "# <<< Still block end <<<";

    public static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "drivers",
        "etc",
        "hosts");

    /// <summary>Returns 0 on success, 1 when the hosts file could not be cleaned.</summary>
    public static int Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        try
        {
            var path = HostsPath;
            if (!File.Exists(path))
            {
                return 0;
            }

            var existing = File.ReadAllText(path);
            var text = RemoveManagedSection(existing);
            if (!string.Equals(text, existing, StringComparison.Ordinal))
            {
                File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                FlushDnsCache();
            }

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    public static bool HasManagedSection(string text) =>
        text.Contains(BeginMarker, StringComparison.Ordinal);

    /// <summary>Removes our marked section and preserves every other line.</summary>
    public static string RemoveManagedSection(string text)
    {
        var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (begin < 0)
        {
            return text;
        }

        var end = text.IndexOf(EndMarker, begin, StringComparison.Ordinal);
        if (end < 0)
        {
            return text[..begin].TrimEnd();
        }

        end += EndMarker.Length;
        var before = text[..begin].TrimEnd();
        var after = text[end..].TrimStart('\r', '\n');
        if (before.Length == 0)
        {
            return after;
        }

        return after.Length == 0
            ? before + Environment.NewLine
            : before + Environment.NewLine + Environment.NewLine + after;
    }

    private static void FlushDnsCache()
    {
        try
        {
            DnsFlushResolverCache();
        }
        catch
        {
            // The hosts file is picked up by new connections even without a flush.
        }
    }

    [DllImport("dnsapi.dll")]
    private static extern int DnsFlushResolverCache();
}
