using RedHoleEngine.Terminal;

namespace RedHoleEngine.Tests.Terminal;

public class VirtualPathTests
{
    [Theory]
    [InlineData("/a/b", "/ignored", "/a/b")]
    [InlineData("b", "/a", "/a/b")]
    [InlineData("./c", "/a", "/a/c")]
    [InlineData("../c", "/a/b", "/a/c")]
    [InlineData("..", "/a", "/")]
    [InlineData("../..", "/a", "/")]
    [InlineData("", "/a/b", "/a/b")]
    [InlineData("   ", "/a", "/a")]
    [InlineData("/a//b/", "/x", "/a/b")]
    [InlineData("a/../../b", "/x", "/b")]
    [InlineData(".", "/", "/")]
    public void Normalize_ResolvesRelativeAndParentSegments(string path, string current, string expected)
    {
        Assert.Equal(expected, VirtualPath.Normalize(path, current));
    }

    [Fact]
    public void Combine_JoinsAndNormalizesBlankSides()
    {
        Assert.Equal("/a/b", VirtualPath.Combine("/a/", "b"));
        Assert.Equal("/a/b", VirtualPath.Combine("/a", "b"));
        Assert.Equal("/b", VirtualPath.Combine("", "b"));
        Assert.Equal("/a", VirtualPath.Combine("/a", ""));
        Assert.Equal("/a", VirtualPath.Combine("   ", "/a"));
    }
}
