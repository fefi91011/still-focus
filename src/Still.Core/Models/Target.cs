namespace Still.Core.Models;

public enum TargetKind
{
    Process,
    Website
}

/// <summary>A single thing that can be blocked: an executable or a domain.</summary>
public sealed class Target
{
    public TargetKind Kind { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Label { get; set; }

    /// <summary>
    /// Websites only: also match every subdomain of <see cref="Value"/>. The hosts file
    /// can only carry exact hosts, so this mainly widens browser and app checks.
    /// </summary>
    public bool IncludeSubdomains { get; set; } = true;

    public Target()
    {
    }

    public Target(TargetKind kind, string value, string? label = null)
    {
        Kind = kind;
        Value = Normalize(kind, value);
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Label)
        ? Kind == TargetKind.Website ? Value : Value + ".exe"
        : Label!;

    public bool MatchesProcess(string? processName)
    {
        if (Kind != TargetKind.Process || string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return string.Equals(Value, NormalizeProcess(processName), StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesHost(string? host)
    {
        if (Kind != TargetKind.Website || string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalized = NormalizeHost(host);
        if (normalized.Equals(Value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IncludeSubdomains
               && normalized.EndsWith("." + Value, StringComparison.OrdinalIgnoreCase);
    }

    public bool SameAs(Target other) => Kind == other.Kind && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public Target Clone() => new(Kind, Value, Label) { IncludeSubdomains = IncludeSubdomains };

    public static string Normalize(TargetKind kind, string? raw) =>
        kind == TargetKind.Process ? NormalizeProcess(raw) : NormalizeHost(raw);

    public static string NormalizeProcess(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim().Trim('"');
        var slash = value.LastIndexOfAny(['\\', '/']);
        if (slash >= 0)
        {
            value = value[(slash + 1)..];
        }

        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        return value.Trim().ToLowerInvariant();
    }

    public static string NormalizeHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim().ToLowerInvariant();
        var scheme = value.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            value = value[(scheme + 3)..];
        }

        var cut = value.IndexOfAny(['/', '?', '#']);
        if (cut >= 0)
        {
            value = value[..cut];
        }

        var at = value.LastIndexOf('@');
        if (at >= 0)
        {
            value = value[(at + 1)..];
        }

        // Keep a possible port out of the comparison, IPv6 literals are not supported.
        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            value = value[..colon];
        }

        value = value.Trim().TrimEnd('.');
        if (value.StartsWith("www.", StringComparison.Ordinal) && value.Length > 4)
        {
            value = value[4..];
        }

        return value.Trim();
    }
}
