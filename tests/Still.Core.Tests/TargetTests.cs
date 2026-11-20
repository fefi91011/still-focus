using Still.Core.Models;
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
