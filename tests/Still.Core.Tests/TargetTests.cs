using Still.Core.Models;
using Still.Core.Presets;
using Xunit;

namespace Still.Core.Tests;

public sealed class TargetTests
{
    [Fact]
    public void Process_Normalization_Strips_Path_And_Extension()
    {
        var target = new Target(TargetKind.Process, @"C:\Program Files\Google\Chrome\Chrome.EXE");
        Assert.Equal("chrome", target.Value);
        Assert.True(target.MatchesProcess("chrome"));
        Assert.True(target.MatchesProcess("Chrome.exe"));
    }

    [Fact]
    public void Website_Normalization_Strips_Scheme_Path_And_Www()
    {
        var target = new Target(TargetKind.Website, "https://www.YouTube.com/watch?v=1");
        Assert.Equal("youtube.com", target.Value);
        Assert.Equal("youtube.com", target.DisplayName);
    }

    [Fact]
    public void Website_Matches_Subdomains()
    {
        var target = new Target(TargetKind.Website, "youtube.com");
        Assert.True(target.MatchesHost("youtube.com"));
        Assert.True(target.MatchesHost("www.youtube.com"));
        Assert.True(target.MatchesHost("m.youtube.com"));
        Assert.False(target.MatchesHost("notyoutube.com"));
        Assert.False(target.MatchesHost("youtube.com.evil.example"));
    }

    [Fact]
    public void Website_Without_Subdomains_Only_Matches_Exact_Host()
    {
        var target = new Target(TargetKind.Website, "youtube.com") { IncludeSubdomains = false };

        Assert.True(target.MatchesHost("youtube.com"));
        Assert.True(target.MatchesHost("www.youtube.com"));
        Assert.False(target.MatchesHost("m.youtube.com"));
    }

    [Fact]
    public void Clone_Keeps_Subdomain_Scope()
    {
        var target = new Target(TargetKind.Website, "youtube.com") { IncludeSubdomains = false };

        Assert.False(target.Clone().IncludeSubdomains);
    }

    [Fact]
    public void Category_Matches_Known_Browsers_And_Paths()
    {
        var browser = TargetCategories.Find("browser")!;
        var games = TargetCategories.Find("games")!;

        Assert.True(browser.MatchesApp("Zen", "zen", @"C:\Users\me\AppData\Local\Programs\Zen\zen.exe"));
        Assert.True(browser.MatchesApp("Vivaldi", "vivaldi", @"C:\Program Files\Vivaldi\Application\vivaldi.exe"));
        Assert.False(browser.MatchesApp("Notepad", "notepad", @"C:\Windows\notepad.exe"));
        Assert.False(browser.MatchesApp("CefSharp.BrowserSubprocess", "cefsharp.browsersubprocess", @"C:\Tools\cef.exe"));

        Assert.True(games.MatchesApp("Some Game", "somegame", @"D:\Steam\steamapps\common\Some Game\game.exe"));
        Assert.True(games.MatchesApp("Minecraft Launcher", "minecraftlauncher", @"C:\X\minecraft.exe"));
        Assert.False(games.MatchesApp("Vivaldi", "vivaldi", @"C:\Program Files\Vivaldi\vivaldi.exe"));
    }

    [Fact]
    public void Allowance_Covers_Subdomains_Of_Rule_Targets()
    {
        var allowance = new Allowance
        {
            Targets = [new Target(TargetKind.Website, "m.youtube.com")]
        };

        Assert.True(allowance.Covers(new Target(TargetKind.Website, "youtube.com")));
        Assert.False(allowance.Covers(new Target(TargetKind.Website, "youtu.be")));
    }
}
