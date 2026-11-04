using RedHoleEngine.Terminal;

namespace RedHoleEngine.Tests.Terminal;

public class TerminalBehaviorTests
{
    [Theory]
    [InlineData("a/b/../c", "/", "/a/c")]
    [InlineData("../..", "/home/user", "/")]
    [InlineData("", "/tmp", "/tmp")]
    [InlineData(".", "/etc", "/etc")]
    [InlineData("/a//b/./c", "/ignored", "/a/b/c")]
    public void Normalize_ResolvesDotsAndParents(string path, string cwd, string expected)
    {
        Assert.Equal(expected, VirtualPath.Normalize(path, cwd));
    }

    [Fact]
    public void Combine_TreatsBlankSidesAsAbsolute()
    {
        Assert.Equal("/a/b", VirtualPath.Combine("/a", "b"));
        Assert.Equal("/a/b", VirtualPath.Combine("/a/", "b"));
        Assert.Equal("/only", VirtualPath.Combine("", "/only"));
        Assert.Equal("/left", VirtualPath.Combine("/left", "  "));
    }

    [Fact]
    public void FileSystem_WriteListRemove_AndStateRoundtrip()
    {
        var fs = new VirtualFileSystem();
        Assert.Equal("/", fs.RootPath);
        Assert.True(fs.IsDirectory("/home/user"));
        Assert.False(fs.Exists("/nope"));

        fs.WriteFile("/home/user/note.txt", "hello", append: false);
        fs.WriteFile("/home/user/note.txt", "!", append: true);
        Assert.Equal("hello!", fs.ReadFile("/home/user/note.txt"));
        Assert.Contains("note.txt", fs.List("/home/user"));
        Assert.Contains("user/", fs.List("/home"));

        fs.CreateFile("/tmp/empty");
        Assert.Equal(string.Empty, fs.ReadFile("/tmp/empty"));

        var dump = fs.DebugDump();
        Assert.Contains("note.txt", dump);

        var state = fs.ToState();
        fs.Remove("/home/user/note.txt", recursive: false);
        Assert.False(fs.Exists("/home/user/note.txt"));

        fs.CreateDirectory("/tmp/nested/deep");
        fs.WriteFile("/tmp/nested/deep/a.txt", "a", false);
        Assert.Throws<InvalidOperationException>(() => fs.Remove("/tmp/nested", recursive: false));
        fs.Remove("/tmp/nested", recursive: true);
        Assert.False(fs.Exists("/tmp/nested"));
        Assert.Throws<InvalidOperationException>(() => fs.Remove("/", false));
        Assert.Throws<InvalidOperationException>(() => fs.Remove("/missing", false));
        Assert.Throws<InvalidOperationException>(() => fs.ReadFile("/missing"));

        fs.LoadState(null);
        Assert.False(fs.Exists("/home"));
        fs.LoadState(state);
        Assert.Equal("hello!", fs.ReadFile("/home/user/note.txt"));

        fs.CreateDirectory("/");
        Assert.True(fs.IsDirectory("/"));
    }

    [Fact]
    public void Session_RunsShellCommands_AndRestoresState()
    {
        var session = new TerminalSession(new VirtualFileSystem());
        Assert.Equal(0, session.Execute("   ").ExitCode);
        Assert.Equal(string.Empty, session.Execute("").Output);

        var help = session.Execute("help");
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("mkdir", help.Output);

        Assert.Contains("/\n", session.Execute("pwd").Output.Replace("\r", ""));
        var ls = session.Execute("ls /");
        Assert.Contains("home/", ls.Output);

        Assert.Equal(0, session.Execute("mkdir /home/user/docs").ExitCode);
        Assert.Equal(0, session.Execute("cd /home/user/docs").ExitCode);
        Assert.Equal("/home/user/docs", session.CurrentDirectory);

        Assert.Equal(0, session.Execute("echo hi there").ExitCode);
        Assert.Contains("hi there", session.Execute("echo hi there").Output);
        Assert.Equal(0, session.Execute("echo line > note.txt").ExitCode);
        Assert.Contains("line", session.Execute("cat note.txt").Output);
        Assert.Equal(0, session.Execute("echo more >> note.txt").ExitCode);
        Assert.Contains("more", session.Execute("cat note.txt").Output);

        Assert.Equal(0, session.Execute("touch plain").ExitCode);
        Assert.Contains("plain", session.Execute("ls").Output);

        var missing = session.Execute("cd /no/such");
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("Directory not found", missing.Output);

        Assert.Equal(1, session.Execute("cat").ExitCode);
        Assert.Equal(1, session.Execute("mkdir").ExitCode);
        Assert.Equal(1, session.Execute("touch").ExitCode);
        Assert.Equal(1, session.Execute("rm").ExitCode);
        Assert.Equal(1, session.Execute("echo hi >").ExitCode);
        Assert.Equal(1, session.Execute("not-a-command").ExitCode);
        Assert.Contains("Unknown command", session.Execute("not-a-command").Output);

        Assert.Equal(0, session.Execute("echo").ExitCode);

        var quoted = session.Execute("echo \"hello world\" tail");
        Assert.Contains("hello world tail", quoted.Output);

        var saved = session.SaveState();
        session.Execute("rm note.txt");
        session.LoadState(saved);
        Assert.Equal("/", session.CurrentDirectory);
        Assert.Contains("line", session.Execute("cat /home/user/docs/note.txt").Output);

        Assert.Equal(0, session.Execute("rm -r /tmp").ExitCode);

        session.LoadState(null);
        Assert.Equal(1, session.Execute("cat /home/user/docs/note.txt").ExitCode);
        Assert.Equal(1, session.Execute("rm /tmp").ExitCode);
    }
}
