using Still.Platform.Windows;
using Xunit;

namespace Still.Core.Tests;

/// <summary>
/// The hosts file is shared with the operating system and the user. These tests
/// pin down that Still only ever touches its own marked section.
/// </summary>
public sealed class HostsFileTests
{
    private const string UserContent =
        "# Copyright (c) 1993-2009 Microsoft Corp.\r\n" +
        "127.0.0.1 localhost\r\n" +
        "::1 localhost\r\n";

    private static string Build(string existing, params string[] domains) =>
        HostsWebsiteBlocker.BuildContent(existing, domains, out _);

    [Fact]
    public void Adds_A_Marked_Section_And_Keeps_User_Entries()
    {
        var text = Build(UserContent, "youtube.com", "reddit.com");

        Assert.Contains("127.0.0.1 localhost", text);
        Assert.Contains("# >>> Still block begin >>>", text);
        Assert.Contains("# <<< Still block end <<<", text);
        Assert.Contains("0.0.0.0 youtube.com", text);
        Assert.Contains("::1 youtube.com", text);
        Assert.Contains("0.0.0.0 www.youtube.com", text);
        Assert.Contains("0.0.0.0 reddit.com", text);
    }

    [Fact]
    public void Reapplying_Replaces_The_Section_Instead_Of_Duplicating_It()
    {
        var first = Build(UserContent, "youtube.com");
        var second = Build(first, "reddit.com");

        Assert.Equal(1, CountOccurrences(second, "# >>> Still block begin >>>"));
        Assert.Equal(1, CountOccurrences(second, "# <<< Still block end <<<"));
        Assert.DoesNotContain("youtube.com", second);
        Assert.Contains("reddit.com", second);
        Assert.Contains("127.0.0.1 localhost", second);
    }

    [Fact]
    public void Removing_All_Domains_Restores_The_Original_File()
    {
        var managed = Build(UserContent, "youtube.com");
        var restored = Build(managed, Array.Empty<string>());

        Assert.DoesNotContain("Still block begin", restored);
        Assert.DoesNotContain("youtube.com", restored);
        Assert.Contains("127.0.0.1 localhost", restored);
        Assert.Contains("::1 localhost", restored);
    }

    [Fact]
    public void Leaves_An_Unmanaged_File_Untouched_When_There_Is_Nothing_To_Block()
    {
        var text = Build(UserContent);

        Assert.Equal(UserContent, text);
    }

    [Fact]
    public void Normalizes_Urls_And_Drops_Invalid_Or_Duplicate_Hosts()
    {
        var text = HostsWebsiteBlocker.BuildContent(
            UserContent,
            ["https://www.YouTube.com/watch?v=1", "youtube.com", "keine-domain", "  "],
            out var entries);

        Assert.Equal(1, entries);
        Assert.Equal(1, CountOccurrences(text, "0.0.0.0 youtube.com"));
        Assert.DoesNotContain("keine-domain", text);
        Assert.DoesNotContain("watch", text);
    }

    [Fact]
    public void Recovers_When_The_End_Marker_Is_Missing()
    {
        var broken = UserContent +
                     "# >>> Still block begin >>>\r\n" +
                     "0.0.0.0 youtube.com\r\n";

        var text = Build(broken, "reddit.com");

        Assert.Equal(1, CountOccurrences(text, "# >>> Still block begin >>>"));
        Assert.Contains("reddit.com", text);
        Assert.Contains("127.0.0.1 localhost", text);
    }

    [Fact]
    public void A_File_With_Only_The_Managed_Section_Is_Removed_Cleanly()
    {
        var managed = Build(string.Empty, "youtube.com");
        var restored = Build(managed);

        Assert.Equal(string.Empty, restored);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
