using Still.Platform.Windows;
using Xunit;

namespace Still.Core.Tests;

/// <summary>
/// Website blocking moved to the browser extension. The hosts-file code remains
/// only to clean up entries written by older Still versions, so these tests
/// focus on preserving user entries while removing our marked section.
/// </summary>
public sealed class LegacyHostsCleanupTests
{
    [Fact]
    public void Removes_Only_The_Managed_Section()
    {
        var existing =
            "127.0.0.1 localhost\r\n" +
            "::1 localhost\r\n" +
            "\r\n" +
            "# >>> Still block begin >>>\r\n" +
            "# Aktualisiert: 2026-03-02 10:00 — verwaltet von Still\r\n" +
            "0.0.0.0 youtube.com\r\n" +
            "::1 youtube.com\r\n" +
            "0.0.0.0 www.youtube.com\r\n" +
            "::1 www.youtube.com\r\n" +
            "# <<< Still block end <<<\r\n" +
            "\r\n" +
            "# Meine Einträge\r\n" +
            "10.0.0.1 nas\r\n";

        var text = LegacyHostsCleanup.RemoveManagedSection(existing);

        Assert.Contains("127.0.0.1 localhost", text);
        Assert.Contains("::1 localhost", text);
        Assert.Contains("10.0.0.1 nas", text);
        Assert.DoesNotContain("youtube.com", text);
        Assert.DoesNotContain("Still block", text);
    }

    [Fact]
    public void Leaves_Files_Without_Our_Markers_Untouched()
    {
        const string existing =
            "127.0.0.1 localhost\r\n" +
            "10.0.0.1 nas\r\n";

        var text = LegacyHostsCleanup.RemoveManagedSection(existing);

        Assert.Equal(existing, text);
        Assert.False(LegacyHostsCleanup.HasManagedSection(existing));
    }

    [Fact]
    public void Removes_An_Open_Ended_Section()
    {
        var existing =
            "127.0.0.1 localhost\r\n" +
            "# >>> Still block begin >>>\r\n" +
            "0.0.0.0 youtube.com\r\n";

        var text = LegacyHostsCleanup.RemoveManagedSection(existing);

        Assert.Contains("127.0.0.1 localhost", text);
        Assert.DoesNotContain("youtube.com", text);
    }

    [Fact]
    public void Recognizes_The_Managed_Section()
    {
        Assert.True(LegacyHostsCleanup.HasManagedSection(
            "# >>> Still block begin >>>\r\n0.0.0.0 youtube.com\r\n# <<< Still block end <<<"));
        Assert.False(LegacyHostsCleanup.HasManagedSection("127.0.0.1 localhost"));
    }
}
