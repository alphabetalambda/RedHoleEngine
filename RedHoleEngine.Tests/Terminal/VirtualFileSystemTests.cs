using RedHoleEngine.Terminal;

namespace RedHoleEngine.Tests.Terminal;

public class VirtualFileSystemTests
{
    [Fact]
    public void Constructor_CreatesStandardDirectories()
    {
        var fs = new VirtualFileSystem();

        Assert.Equal("/", fs.RootPath);
        Assert.True(fs.Exists("/"));
        Assert.True(fs.IsDirectory("/"));
        Assert.True(fs.IsDirectory("/home/user"));
        Assert.True(fs.IsDirectory("/etc"));
        Assert.True(fs.IsDirectory("/tmp"));
        Assert.False(fs.Exists("/missing"));
        Assert.False(fs.IsDirectory("/missing"));
        Assert.Equal(new[] { "etc/", "home/", "tmp/" }, fs.List("/"));
    }

    [Fact]
    public void Files_CreateWriteAppendReadAndRemove()
    {
        var fs = new VirtualFileSystem();

        fs.CreateFile("/tmp/new.txt");
        Assert.Equal(string.Empty, fs.ReadFile("/tmp/new.txt"));
        Assert.True(fs.Exists("/tmp/new.txt"));
        Assert.False(fs.IsDirectory("/tmp/new.txt"));

        fs.WriteFile("/tmp/new.txt", "one", append: false);
        fs.WriteFile("/tmp/new.txt", "two", append: true);
        Assert.Equal("onetwo", fs.ReadFile("/tmp/new.txt"));

        fs.WriteFile("/etc/note.txt", "keep", append: false);
        Assert.Equal(new[] { "note.txt" }, fs.List("/etc"));

        fs.Remove("/tmp/new.txt", recursive: false);
        Assert.False(fs.Exists("/tmp/new.txt"));
        var missing = Assert.Throws<InvalidOperationException>(() => fs.ReadFile("/tmp/new.txt"));
        Assert.Equal("File not found", missing.Message);
    }

    [Fact]
    public void Directories_CreateNestedListAndRemove()
    {
        var fs = new VirtualFileSystem();

        fs.CreateDirectory("/");
        fs.CreateDirectory("/var/log");
        fs.CreateFile("/var/log/app.txt");

        Assert.Equal(new[] { "log/" }, fs.List("/var"));
        Assert.Equal(new[] { "app.txt" }, fs.List("/var/log"));
        Assert.Contains("var/", fs.DebugDump());
        Assert.Contains("log/", fs.DebugDump());
        Assert.Contains("app.txt", fs.DebugDump());

        var blocked = Assert.Throws<InvalidOperationException>(() => fs.Remove("/var", recursive: false));
        Assert.Equal("Directory not empty. Use -r to remove.", blocked.Message);
        Assert.True(fs.Exists("/var/log/app.txt"));

        fs.Remove("/var", recursive: true);
        Assert.False(fs.Exists("/var"));

        fs.CreateDirectory("/empty");
        fs.Remove("/empty", recursive: false);
        Assert.False(fs.Exists("/empty"));

        var missing = Assert.Throws<InvalidOperationException>(() => fs.Remove("/var", recursive: true));
        Assert.Equal("Path not found", missing.Message);

        var root = Assert.Throws<InvalidOperationException>(() => fs.Remove("/", recursive: true));
        Assert.Equal("Cannot remove root", root.Message);

        fs.CreateFile("/etc/file.txt");
        var listedFile = Assert.Throws<InvalidOperationException>(() => fs.List("/etc/file.txt"));
        Assert.Equal("Directory not found", listedFile.Message);
        Assert.Equal("Directory not found", Assert.Throws<InvalidOperationException>(() => fs.WriteFile("/missing/file.txt", "x", false)).Message);
        Assert.Equal("File not found", Assert.Throws<InvalidOperationException>(() => fs.ReadFile("/etc")).Message);
    }

    [Fact]
    public void SameNameFileAndDirectory_RemoveDeletesTheFileFirst()
    {
        var fs = new VirtualFileSystem();
        fs.CreateDirectory("/tmp/box");
        fs.CreateFile("/tmp/box");

        Assert.True(fs.IsDirectory("/tmp/box"));
        Assert.Equal("File not found", Assert.Throws<InvalidOperationException>(() => fs.ReadFile("/tmp/box")).Message);
        Assert.Contains("box/", fs.List("/tmp"));
        Assert.Contains("box", fs.List("/tmp"));

        fs.Remove("/tmp/box", recursive: false);

        Assert.True(fs.IsDirectory("/tmp/box"));
        Assert.DoesNotContain("box", fs.List("/tmp").Where(entry => entry == "box"));
    }

    [Fact]
    public void State_RoundTripsAndNullLoadClearsDefaults()
    {
        var fs = new VirtualFileSystem();
        fs.WriteFile("/home/user/profile", "ada", append: false);
        fs.CreateDirectory("/opt/bin");

        var state = fs.ToState();
        Assert.Equal("/", state.Root.Name);

        var restored = new VirtualFileSystem();
        restored.LoadState(state);
        Assert.Equal("ada", restored.ReadFile("/home/user/profile"));
        Assert.True(restored.IsDirectory("/opt/bin"));
        Assert.True(restored.IsDirectory("/etc"));

        restored.LoadState(new VirtualFileSystemState());
        Assert.False(restored.Exists("/home"));
        Assert.Empty(restored.List("/"));

        restored.CreateDirectory("/tmp");
        restored.LoadState(null);
        Assert.True(restored.Exists("/"));
        Assert.False(restored.Exists("/tmp"));
        Assert.Empty(restored.List("/"));
    }
}
