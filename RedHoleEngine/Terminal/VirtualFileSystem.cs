using System.Text;

namespace RedHoleEngine.Terminal;

public sealed class VirtualFileSystem
{
    private readonly VirtualDirectory _root = new("/");

    public string RootPath => "/";

    public VirtualFileSystem()
    {
        CreateDirectory("/home");
        CreateDirectory("/home/user");
        CreateDirectory("/etc");
        CreateDirectory("/tmp");
    }

    public bool Exists(string path)
    {
        return FindNode(path) != null;
    }

    public bool IsDirectory(string path)
    {
        return FindNode(path) is VirtualDirectory;
    }

    public string ReadFile(string path)
    {
        var node = FindNode(path) as VirtualFile;
        if (node == null)
            throw new InvalidOperationException("File not found");
        return node.Contents;
    }

    public void WriteFile(string path, string contents, bool append)
    {
        var normalized = VirtualPath.Normalize(path, "/");
        var parentPath = GetParentPath(normalized);
        var fileName = GetLeafName(normalized);
        var parent = GetDirectory(parentPath);

        if (parent.Files.TryGetValue(fileName, out var existing))
        {
            existing.Contents = append ? existing.Contents + contents : contents;
            return;
        }

        parent.Files[fileName] = new VirtualFile(fileName, contents);
    }

    public void CreateFile(string path)
    {
        WriteFile(path, string.Empty, append: false);
    }

    public void CreateDirectory(string path)
    {
        var normalized = VirtualPath.Normalize(path, "/");
        if (normalized == "/")
            return;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = _root;
        foreach (var part in parts)
        {
            if (!current.Directories.TryGetValue(part, out var next))
            {
                next = new VirtualDirectory(part);
                current.Directories[part] = next;
            }
            current = next;
        }
    }

    public IEnumerable<string> List(string path)
    {
        var dir = GetDirectory(path);
        var entries = new List<string>();
        entries.AddRange(dir.Directories.Keys.OrderBy(x => x).Select(x => x + "/"));
        entries.AddRange(dir.Files.Keys.OrderBy(x => x));
        return entries;
    }

    public void Remove(string path, bool recursive)
    {
        var normalized = VirtualPath.Normalize(path, "/");
        if (normalized == "/")
            throw new InvalidOperationException("Cannot remove root");

        var parentPath = GetParentPath(normalized);
        var name = GetLeafName(normalized);
        var parent = GetDirectory(parentPath);

        if (parent.Files.Remove(name))
            return;

        if (!parent.Directories.TryGetValue(name, out var dir))
            throw new InvalidOperationException("Path not found");

        if (!recursive && (dir.Directories.Count > 0 || dir.Files.Count > 0))
            throw new InvalidOperationException("Directory not empty. Use -r to remove.");

        parent.Directories.Remove(name);
    }

    public VirtualFileSystemState ToState()
    {
        return new VirtualFileSystemState
        {
            Root = ToStateDirectory(_root)
        };
    }

    public void LoadState(VirtualFileSystemState? state)
    {
        _root.Directories.Clear();
        _root.Files.Clear();

        if (state == null)
            return;

        LoadDirectory(_root, state.Root);
    }

    public string DebugDump()
    {
        var builder = new StringBuilder();
        DumpDirectory(builder, _root, 0);
        return builder.ToString();
    }

    private static void DumpDirectory(StringBuilder builder, VirtualDirectory directory, int depth)
    {
        var indent = new string(' ', depth * 2);
        foreach (var subdir in directory.Directories.Values.OrderBy(x => x.Name))
        {
            builder.AppendLine($"{indent}{subdir.Name}/");
            DumpDirectory(builder, subdir, depth + 1);
        }
        foreach (var file in directory.Files.Values.OrderBy(x => x.Name))
        {
            builder.AppendLine($"{indent}{file.Name}");
        }
    }

    private VirtualDirectory GetDirectory(string path)
    {
        var node = FindNode(path);
        if (node is VirtualDirectory dir)
            return dir;

        throw new InvalidOperationException("Directory not found");
    }

    private VirtualNode? FindNode(string path)
    {
        var normalized = VirtualPath.Normalize(path, "/");
        if (normalized == "/")
            return _root;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        VirtualDirectory current = _root;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == parts.Length - 1)
            {
                if (current.Directories.TryGetValue(part, out var dir))
                    return dir;
                if (current.Files.TryGetValue(part, out var file))
                    return file;
                return null;
            }

            if (!current.Directories.TryGetValue(part, out var next))
                return null;
            current = next;
        }

        return current;
    }

    private static string GetParentPath(string path)
    {
        var normalized = VirtualPath.Normalize(path, "/");
        if (normalized == "/")
            return "/";

        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash <= 0)
            return "/";

        return normalized[..lastSlash];
    }

    private static string GetLeafName(string path)
    {
        var normalized = VirtualPath.Normalize(path, "/");
        if (normalized == "/")
            return "/";

        var lastSlash = normalized.LastIndexOf('/');
        return normalized[(lastSlash + 1)..];
    }

    private static VirtualDirectoryState ToStateDirectory(VirtualDirectory directory)
    {
        return new VirtualDirectoryState(directory.Name)
        {
            Directories = directory.Directories.Values.OrderBy(x => x.Name).Select(ToStateDirectory).ToList(),
            Files = directory.Files.Values.OrderBy(x => x.Name).Select(file => new VirtualFileState(file.Name, file.Contents)).ToList()
        };
    }

    private static void LoadDirectory(VirtualDirectory target, VirtualDirectoryState state)
    {
        foreach (var dir in state.Directories)
        {
            var newDir = new VirtualDirectory(dir.Name);
            target.Directories[dir.Name] = newDir;
            LoadDirectory(newDir, dir);
        }

        foreach (var file in state.Files)
        {
            target.Files[file.Name] = new VirtualFile(file.Name, file.Contents);
        }
    }

    private abstract class VirtualNode
    {
        protected VirtualNode(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class VirtualDirectory : VirtualNode
    {
        public VirtualDirectory(string name) : base(name)
        {
        }

        public Dictionary<string, VirtualDirectory> Directories { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, VirtualFile> Files { get; } = new(StringComparer.Ordinal);
    }

    private sealed class VirtualFile : VirtualNode
    {
        public VirtualFile(string name, string contents) : base(name)
        {
            Contents = contents;
        }

        public string Contents { get; set; }
    }
}
