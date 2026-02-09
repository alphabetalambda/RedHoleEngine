using System.Text;

namespace RedHoleEngine.Terminal;

public sealed class TerminalSession
{
    private readonly VirtualFileSystem _fileSystem;
    private readonly StringBuilder _output = new();

    public TerminalSession(VirtualFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        CurrentDirectory = "/";
    }

    public string CurrentDirectory { get; private set; }

    public TerminalCommandResult Execute(string commandLine)
    {
        _output.Clear();

        if (string.IsNullOrWhiteSpace(commandLine))
            return new TerminalCommandResult(string.Empty);

        var parts = SplitArguments(commandLine);
        if (parts.Count == 0)
            return new TerminalCommandResult(string.Empty);

        var command = parts[0];
        var args = parts.Skip(1).ToList();

        try
        {
            switch (command)
            {
                case "help":
                    WriteLine("Commands: ls, cd, pwd, cat, echo, mkdir, touch, rm, help");
                    break;
                case "pwd":
                    WriteLine(CurrentDirectory);
                    break;
                case "ls":
                    ListCommand(args);
                    break;
                case "cd":
                    ChangeDirectory(args);
                    break;
                case "cat":
                    CatCommand(args);
                    break;
                case "echo":
                    EchoCommand(args);
                    break;
                case "mkdir":
                    MkdirCommand(args);
                    break;
                case "touch":
                    TouchCommand(args);
                    break;
                case "rm":
                    RmCommand(args);
                    break;
                default:
                    WriteLine($"Unknown command: {command}");
                    return new TerminalCommandResult(_output.ToString(), 1);
            }
        }
        catch (Exception ex)
        {
            WriteLine(ex.Message);
            return new TerminalCommandResult(_output.ToString(), 1);
        }

        return new TerminalCommandResult(_output.ToString());
    }

    public VirtualFileSystemState SaveState()
    {
        return _fileSystem.ToState();
    }

    public void LoadState(VirtualFileSystemState? state)
    {
        _fileSystem.LoadState(state);
        CurrentDirectory = "/";
    }

    private void ListCommand(List<string> args)
    {
        var path = args.Count > 0 ? args[0] : CurrentDirectory;
        var normalized = VirtualPath.Normalize(path, CurrentDirectory);
        foreach (var entry in _fileSystem.List(normalized))
        {
            WriteLine(entry);
        }
    }

    private void ChangeDirectory(List<string> args)
    {
        var target = args.Count > 0 ? args[0] : "/";
        var normalized = VirtualPath.Normalize(target, CurrentDirectory);
        if (!_fileSystem.IsDirectory(normalized))
            throw new InvalidOperationException("Directory not found");

        CurrentDirectory = normalized;
    }

    private void CatCommand(List<string> args)
    {
        if (args.Count == 0)
            throw new InvalidOperationException("Missing file path");

        var path = VirtualPath.Normalize(args[0], CurrentDirectory);
        WriteLine(_fileSystem.ReadFile(path));
    }

    private void EchoCommand(List<string> args)
    {
        if (args.Count == 0)
        {
            WriteLine(string.Empty);
            return;
        }

        int redirectIndex = args.FindIndex(x => x == ">" || x == ">>");
        if (redirectIndex >= 0)
        {
            var text = string.Join(' ', args.Take(redirectIndex));
            bool append = args[redirectIndex] == ">>";
            if (redirectIndex + 1 >= args.Count)
                throw new InvalidOperationException("Missing redirect path");

            var path = VirtualPath.Normalize(args[redirectIndex + 1], CurrentDirectory);
            _fileSystem.WriteFile(path, text + "\n", append);
            return;
        }

        WriteLine(string.Join(' ', args));
    }

    private void MkdirCommand(List<string> args)
    {
        if (args.Count == 0)
            throw new InvalidOperationException("Missing directory path");

        var path = VirtualPath.Normalize(args[0], CurrentDirectory);
        _fileSystem.CreateDirectory(path);
    }

    private void TouchCommand(List<string> args)
    {
        if (args.Count == 0)
            throw new InvalidOperationException("Missing file path");

        var path = VirtualPath.Normalize(args[0], CurrentDirectory);
        if (_fileSystem.Exists(path))
            return;

        _fileSystem.CreateFile(path);
    }

    private void RmCommand(List<string> args)
    {
        if (args.Count == 0)
            throw new InvalidOperationException("Missing path");

        bool recursive = args.Contains("-r") || args.Contains("-rf") || args.Contains("-fr");
        var pathArg = args.FirstOrDefault(x => !x.StartsWith("-"));
        if (string.IsNullOrWhiteSpace(pathArg))
            throw new InvalidOperationException("Missing path");

        var path = VirtualPath.Normalize(pathArg, CurrentDirectory);
        _fileSystem.Remove(path, recursive);
    }

    private void WriteLine(string value)
    {
        _output.AppendLine(value);
    }

    private static List<string> SplitArguments(string input)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args;
    }
}
