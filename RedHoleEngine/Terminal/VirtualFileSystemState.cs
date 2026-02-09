namespace RedHoleEngine.Terminal;

public sealed class VirtualFileSystemState
{
    public VirtualDirectoryState Root { get; set; } = new("/");
}

public sealed class VirtualDirectoryState
{
    public VirtualDirectoryState(string name)
    {
        Name = name;
    }

    public string Name { get; set; }
    public List<VirtualDirectoryState> Directories { get; set; } = new();
    public List<VirtualFileState> Files { get; set; } = new();
}

public sealed class VirtualFileState
{
    public VirtualFileState(string name, string contents)
    {
        Name = name;
        Contents = contents;
    }

    public string Name { get; set; }
    public string Contents { get; set; }
}
