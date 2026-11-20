using System.Runtime.InteropServices;
using System.Text;
using Still.Core.Models;
using Still.Core.Runtime;

namespace Still.Platform.Windows;

/// <summary>
/// Blocks domains by maintaining one clearly marked section in the Windows hosts
/// file. User entries outside this section are preserved. Removing the section is
/// enough to restore the previous state, so a crash never permanently edits hosts.
/// </summary>
public sealed class HostsWebsiteBlocker : IWebsiteBlocker
{
    private const string BeginMarker = "# >>> Still block begin >>>";
    private const string EndMarker = "# <<< Still block end <<<";

    private readonly string _hostsPath;

    public HostsWebsiteBlocker(string? hostsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(hostsPath))
        {
            _hostsPath = hostsPath;
            return;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        _hostsPath = Path.Combine(windows, "System32", "drivers", "etc", "hosts");
    }

    public string HostsPath => _hostsPath;

    public bool CanBlock => OperatingSystem.IsWindows() && WindowsElevation.IsElevated;

    public string? UnavailableReason => !OperatingSystem.IsWindows()
        ? "Website-Sperren sind nur unter Windows verfügbar."
        : CanBlock
            ? null
            : "Website-Sperren benötigen Administratorrechte. Starte Still als Administrator oder nutze den eingerichteten Autostart.";

    public WebsiteApplyResult Apply(IReadOnlyCollection<string> domains)
    {
        if (!OperatingSystem.IsWindows())
        {
            return WebsiteApplyResult.Failure("Website-Sperren sind nur unter Windows verfügbar.");
        }

        if (!WindowsElevation.IsElevated)
        {
            return WebsiteApplyResult.Failure(
                "Website-Sperren benötigen Administratorrechte. Die Programme-Sperre funktioniert weiterhin.");
        }

        try
        {
            var existing = File.Exists(_hostsPath) ? File.ReadAllText(_hostsPath) : string.Empty;
            var text = BuildContent(existing, domains, out var entries);

            if (!string.Equals(text, existing, StringComparison.Ordinal))
            {
                File.WriteAllText(_hostsPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                FlushDnsCache();
            }

            return WebsiteApplyResult.Ok(entries);
        }
        catch (UnauthorizedAccessException)
        {
            return WebsiteApplyResult.Failure(
                "Keine Schreibrechte für die Hosts-Datei. Bitte Still als Administrator starten.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return WebsiteApplyResult.Failure("Hosts-Datei konnte nicht geändert werden: " + ex.Message);
        }
    }

    /// <summary>
    /// Pure transformation of the current hosts file: keeps every user entry,
    /// replaces our marked section and removes it when no domain remains.
    /// </summary>
    internal static string BuildContent(
        string existing,
        IEnumerable<string> domains,
        out int entries)
    {
        var hadManagedSection = existing.Contains(BeginMarker, StringComparison.Ordinal);
        var clean = RemoveManagedSection(existing);

        var hosts = domains
            .Select(Target.NormalizeHost)
            .Where(IsValidHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToList();

        entries = hosts.Count;
        if (hosts.Count == 0 && !hadManagedSection)
        {
            return existing;
        }

        return BuildHostsFile(clean, hosts);
    }

    internal static string BuildHostsFile(string clean, IReadOnlyList<string> hosts)
    {
        var builder = new StringBuilder();
        var prefix = clean.TrimEnd('\r', '\n');
        if (prefix.Length > 0)
        {
            builder.Append(prefix);
            builder.AppendLine();
            builder.AppendLine();
        }

        if (hosts.Count > 0)
        {
            builder.AppendLine(BeginMarker);
            builder.AppendLine($"# Aktualisiert: {DateTimeOffset.Now:yyyy-MM-dd HH:mm} — verwaltet von Still");
            foreach (var host in hosts)
            {
                // Both address families fail fast. The www variant covers the
                // most common redirect target without letting it slip through.
                builder.AppendLine($"0.0.0.0 {host}");
                builder.AppendLine($"::1 {host}");
                builder.AppendLine($"0.0.0.0 www.{host}");
                builder.AppendLine($"::1 www.{host}");
            }

            builder.AppendLine(EndMarker);
        }

        return builder.ToString();
    }

    internal static string RemoveManagedSection(string text)
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

    internal static bool IsValidHost(string host) =>
        host.Length is > 0 and <= 253
        && host.Contains('.')
        && host.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');

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

    [DllImport("dnsapi.dll", SetLastError = true)]
    private static extern int DnsFlushResolverCache();
}
