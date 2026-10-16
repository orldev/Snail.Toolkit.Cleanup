using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Tests;

public sealed class GlobTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Theory]
    [InlineData("/a/*/c", "/a/b/c", true)]
    [InlineData("/a/*/c", "/a/b/d/c", false)]
    [InlineData("/a/**/c", "/a/b/d/c", true)]
    [InlineData("/a/**", "/a", true)]
    [InlineData("/a/snail-*", "/a/snail-me-1", true)]
    [InlineData("/a/snail-*", "/a/other", false)]
    [InlineData("/a/*.log", "/a/x.log", true)]
    [InlineData("/a/*-*-1", "/a/snail-me-1", true)]
    [InlineData("/a/*-*-1", "/a/snail-1", false)]
    [InlineData("/a/**/c/*.log", "/a/b/c/d/c/x.log", true)]
    [InlineData("/a/**/c", "/a/c", true)]
    [InlineData("/a/b", "/a//b/", true)]
    [InlineData("/a/b", "/a/bc", false)]
    [InlineData("/a/b/*", "/a/b", false)]
    [InlineData("*/b", "/a/b", true)]
    public void Pattern_MatchesPathsSegmentBySegment(string pattern, string path, bool expected) =>
        Assert.Equal(expected, Glob.Matches(pattern, path));

    [Fact]
    public void Tilde_StandsForHome() =>
        Assert.True(Glob.Matches("~/Library/*", $"{Home}/Library/Caches"));

    [Fact]
    public void Root_AndDepth_SayWhereATrailHasToLook()
    {
        Assert.Equal($"{Home}/Library/Application Support/Code/WebStorage", Glob.Root("~/Library/Application Support/Code/WebStorage/*"));
        Assert.Equal(1, Glob.Depth("~/Library/Application Support/Code/WebStorage/*"));
        Assert.Null(Glob.Depth("~/.claude/**"));
    }
}
