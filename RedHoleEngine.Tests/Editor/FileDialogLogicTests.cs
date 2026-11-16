using System.Reflection;
using RedHoleEngine.Editor.UI;

namespace RedHoleEngine.Tests.Editor;

public class FileDialogLogicTests : IDisposable
{
    private readonly string _tempDir;

    public FileDialogLogicTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rh_filedialog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ParseFilter_ReadsDescriptionPairsAndMultiplePatterns()
    {
        var filters = FileDialogLogic.ParseFilter("Images (*.png;*.JPG)|*.png;*.JPG|Scenes|*.rhes|All Files (*.*)|*.*");

        Assert.Equal(3, filters.Count);
        Assert.Equal("Images (*.png;*.JPG)", filters[0].Description);
        Assert.Equal(new[] { ".png", ".JPG" }, filters[0].Extensions);
        Assert.Equal(new[] { ".rhes" }, filters[1].Extensions);
        Assert.True(filters[2].MatchesAll);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void ParseFilter_EmptyMatchesEverything(string? filter)
    {
        var filters = FileDialogLogic.ParseFilter(filter);
        Assert.Single(filters);
        Assert.True(filters[0].MatchesAll);
    }

    [Fact]
    public void ParseFilter_AcceptsBarePatternList()
    {
        var filters = FileDialogLogic.ParseFilter("*.csv;tsv");
        Assert.Single(filters);
        Assert.Equal(new[] { ".csv", ".tsv" }, filters[0].Extensions);
    }

    [Theory]
    [InlineData("photo.PNG", true)]
    [InlineData("photo.png.bak", false)]
    [InlineData("notes.txt", false)]
    [InlineData("scan.jpg", true)]
    public void Matches_IsCaseInsensitiveExtensionMatch(string name, bool expected)
    {
        var filter = FileDialogLogic.ParseFilter("Images|*.png;*.jpg")[0];
        Assert.Equal(expected, FileDialogLogic.Matches(filter, name));
    }

    [Fact]
    public void IsHidden_DotPrefixed()
    {
        Assert.True(FileDialogLogic.IsHidden(".git"));
        Assert.False(FileDialogLogic.IsHidden("src"));
    }

    [Fact]
    public void ResolveInput_CombinesRelativeAndKeepsAbsolute()
    {
        Assert.Equal(Path.Combine(_tempDir, "a.rhes"), FileDialogLogic.ResolveInput(_tempDir, "a.rhes"));
        Assert.Equal(Path.Combine(_tempDir, "x.rhes"), FileDialogLogic.ResolveInput("/somewhere/else", Path.Combine(_tempDir, "x.rhes")));
        Assert.Equal(Path.GetDirectoryName(_tempDir), FileDialogLogic.ResolveInput(_tempDir, ".."));
    }

    [Fact]
    public void ResolveInput_ExpandsHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.GetFullPath(Path.Combine(home, "docs")), FileDialogLogic.ResolveInput(_tempDir, "~/docs"));
        Assert.Equal(Path.GetFullPath(home), FileDialogLogic.ResolveInput(_tempDir, "~"));
    }

    [Fact]
    public void ApplyDefaultExtension_AddsWhenMissingOrNotAccepted()
    {
        var scenes = FileDialogLogic.ParseFilter("Scenes|*.rhes")[0];
        var all = FileDialogLogic.ParseFilter("All|*.*")[0];

        Assert.Equal("level.rhes", FileDialogLogic.ApplyDefaultExtension("level", ".rhes", scenes));
        Assert.Equal("level.rhes", FileDialogLogic.ApplyDefaultExtension("level.rhes", ".rhes", scenes));
        Assert.Equal("level.RHES", FileDialogLogic.ApplyDefaultExtension("level.RHES", ".rhes", scenes));
        Assert.Equal("level.v2.rhes", FileDialogLogic.ApplyDefaultExtension("level.v2", ".rhes", scenes));
        Assert.Equal("level.v2", FileDialogLogic.ApplyDefaultExtension("level.v2", ".rhes", all));
        Assert.Equal("level.rhes", FileDialogLogic.ApplyDefaultExtension("level", "rhes", null));
        Assert.Equal("level", FileDialogLogic.ApplyDefaultExtension("level", "", scenes));
    }

    [Fact]
    public void GetBreadcrumbs_BuildsCumulativePaths()
    {
        var nested = Path.Combine(_tempDir, "a", "b");
        var crumbs = FileDialogLogic.GetBreadcrumbs(nested);

        Assert.Equal(Path.GetPathRoot(nested), crumbs[0].Path);
        Assert.Equal(nested, crumbs[^1].Path);
        Assert.Equal("b", crumbs[^1].Label);
        Assert.Equal("a", crumbs[^2].Label);
        Assert.Equal(Path.Combine(_tempDir, "a"), crumbs[^2].Path);
        Assert.All(crumbs, c => Assert.False(string.IsNullOrEmpty(c.Label)));
    }

    [Fact]
    public void FindInAncestors_WalksUpToMatch()
    {
        var deep = Path.Combine(_tempDir, "x", "y", "z");
        Directory.CreateDirectory(deep);
        var marker = Path.Combine(_tempDir, "x", "MARKER.md");
        File.WriteAllText(marker, "hi");

        Assert.Equal(marker, FileDialogLogic.FindInAncestors(deep, "MARKER.md"));
        Assert.Null(FileDialogLogic.FindInAncestors(deep, "MARKER.md", maxLevels: 1));
        Assert.Null(FileDialogLogic.FindInAncestors(deep, "does-not-exist-" + Guid.NewGuid()));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+0123456789abcdef", "1.2.3 (0123456)")]
    [InlineData("1.0.0+abc", "1.0.0 (abc)")]
    [InlineData("2.0.0-beta+", "2.0.0-beta")]
    public void FormatInformationalVersion_ShortensCommitHash(string input, string expected)
    {
        Assert.Equal(expected, AppInfo.FormatInformationalVersion(input));
    }

    [Fact]
    public void GetVersion_ReturnsSomethingForEditorAssembly()
    {
        var version = AppInfo.GetVersion(typeof(FileDialog).Assembly);
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain("+", version);
    }

    [Fact]
    public void ReadLicenseSummary_JoinsFirstNonEmptyLines()
    {
        var license = Path.Combine(_tempDir, "LICENSE");
        File.WriteAllLines(license, new[] { "", "   Example License   ", "  Version 2.0", "", "Terms..." });

        Assert.Equal("Example License, Version 2.0", AppInfo.ReadLicenseSummary(license));
        Assert.Null(AppInfo.ReadLicenseSummary(Path.Combine(_tempDir, "missing")));
        Assert.Null(AppInfo.ReadLicenseSummary(null));
    }

    [Fact]
    public void RepositoryLicense_IsFoundFromTestOutput()
    {
        // The test binaries live under the repository, so the About dialog's lookup should find it
        var license = FileDialogLogic.FindInAncestors(AppContext.BaseDirectory, "LICENSE");
        Assert.NotNull(license);
        Assert.NotNull(AppInfo.ReadLicenseSummary(license));
    }

    [Fact]
    public void TryOpenWithShell_RejectsEmptyTarget()
    {
        Assert.False(AppInfo.TryOpenWithShell("  ", out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
