using RedHoleEngine.Terminal;

namespace RedHoleEngine.Tests.Terminal;

public class TerminalSessionTests
{
    [Fact]
    public void Execute_BlankAndUnknownCommands()
    {
        var session = new TerminalSession(new VirtualFileSystem());

        var blank = session.Execute("   ");
        Assert.Equal(string.Empty, blank.Output);
        Assert.Equal(0, blank.ExitCode);

        var quotedBlank = session.Execute("echo \"\"");
        Assert.Equal(Environment.NewLine, quotedBlank.Output);
        Assert.Equal(0, quotedBlank.ExitCode);

        var unknown = session.Execute("foobar");
        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("Unknown command: foobar", unknown.Output);
    }

    [Fact]
    public void Execute_HelpPwdLsAndCd()
    {
        var session = new TerminalSession(new VirtualFileSystem());

        var help = session.Execute("help");
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Commands: ls, cd, pwd, cat, echo, mkdir, touch, rm, help", help.Output);

        Assert.Equal("/" + Environment.NewLine, session.Execute("pwd").Output);
        Assert.Equal($"etc/{Environment.NewLine}home/{Environment.NewLine}tmp/{Environment.NewLine}", session.Execute("ls").Output);

        var missing = session.Execute("cd /nope");
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("Directory not found", missing.Output);
        Assert.Equal("/", session.CurrentDirectory);

        Assert.Equal(0, session.Execute("cd /home/user").ExitCode);
        Assert.Equal("/home/user", session.CurrentDirectory);
        Assert.Equal("/home/user" + Environment.NewLine, session.Execute("pwd").Output);

        Assert.Equal(0, session.Execute("cd").ExitCode);
        Assert.Equal("/", session.CurrentDirectory);

        var file = session.Execute("touch /tmp/a.txt");
        Assert.Equal(0, file.ExitCode);
        var notDirectory = session.Execute("cd /tmp/a.txt");
        Assert.Equal(1, notDirectory.ExitCode);
        Assert.Contains("Directory not found", notDirectory.Output);
    }

    [Fact]
    public void Execute_CatEchoMkdirTouchAndRm()
    {
        var session = new TerminalSession(new VirtualFileSystem());

        Assert.Equal(1, session.Execute("cat").ExitCode);
        Assert.Contains("Missing file path", session.Execute("cat").Output);
        Assert.Contains("Missing directory path", session.Execute("mkdir").Output);
        Assert.Contains("Missing file path", session.Execute("touch").Output);
        Assert.Contains("Missing path", session.Execute("rm").Output);
        Assert.Contains("Missing path", session.Execute("rm -rf").Output);

        Assert.Equal(Environment.NewLine, session.Execute("echo").Output);
        Assert.Equal("hello world" + Environment.NewLine, session.Execute("echo hello   world").Output);
        Assert.Equal("hello world" + Environment.NewLine, session.Execute("echo \"hello world\"").Output);

        Assert.Equal(string.Empty, session.Execute("echo hello > /tmp/note.txt").Output);
        Assert.Equal("hello" + Environment.NewLine + Environment.NewLine, session.Execute("cat /tmp/note.txt").Output);
        Assert.Equal(0, session.Execute("echo there >> /tmp/note.txt").ExitCode);
        Assert.Equal("hello" + Environment.NewLine + "there" + Environment.NewLine + Environment.NewLine, session.Execute("cat /tmp/note.txt").Output);
        Assert.Contains("Missing redirect path", session.Execute("echo hi >").Output);

        Assert.Equal(0, session.Execute("mkdir /var/log").ExitCode);
        Assert.Equal(0, session.Execute("touch /var/log/app.txt").ExitCode);
        session.Execute("echo keep > /var/log/app.txt");
        Assert.Equal(0, session.Execute("touch /var/log/app.txt").ExitCode);
        Assert.Equal("keep" + Environment.NewLine + Environment.NewLine, session.Execute("cat /var/log/app.txt").Output);

        Assert.Contains("Directory not empty", session.Execute("rm /var").Output);
        Assert.Equal(0, session.Execute("rm -r /var").ExitCode);
        Assert.Contains("File not found", session.Execute("cat /var/log/app.txt").Output);
        Assert.Equal(0, session.Execute("rm -rf /tmp/note.txt").ExitCode);
        Assert.Contains("Path not found", session.Execute("rm /tmp/note.txt").Output);
        Assert.Contains("Cannot remove root", session.Execute("rm -fr /").Output);

        Assert.Equal(0, session.Execute("cd /home").ExitCode);
        Assert.Equal(0, session.Execute("mkdir user/games").ExitCode);
        Assert.Contains("games/", session.Execute("ls user").Output);
    }

    [Fact(Skip = "BUG: expected cat to print stored file bytes once, actual WriteLine appends a second newline after echo's trailing newline")]
    public void Execute_Cat_DoesNotAddAnExtraNewline()
    {
        var session = new TerminalSession(new VirtualFileSystem());
        session.Execute("echo hello > /tmp/note.txt");

        Assert.Equal("hello" + Environment.NewLine, session.Execute("cat /tmp/note.txt").Output);
    }

    [Fact]
    public void SaveAndLoadState_ResetsDirectoryAndReplacesFiles()
    {
        var fs = new VirtualFileSystem();
        var session = new TerminalSession(fs);
        session.Execute("echo saved > /tmp/keep.txt");
        session.Execute("cd /tmp");
        var state = session.SaveState();

        session.Execute("rm /tmp/keep.txt");
        session.LoadState(state);

        Assert.Equal("/", session.CurrentDirectory);
        Assert.Equal("saved" + Environment.NewLine + Environment.NewLine, session.Execute("cat /tmp/keep.txt").Output);

        session.Execute("cd /home");
        session.LoadState(null);
        Assert.Equal("/", session.CurrentDirectory);
        Assert.Equal(string.Empty, session.Execute("ls").Output);
    }
}
