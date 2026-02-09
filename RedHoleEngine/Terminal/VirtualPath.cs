using System.Text;

namespace RedHoleEngine.Terminal;

public static class VirtualPath
{
    public static string Normalize(string path, string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NormalizeAbsolute(currentDirectory);

        if (!path.StartsWith("/"))
        {
            path = Combine(currentDirectory, path);
        }

        return NormalizeAbsolute(path);
    }

    public static string Combine(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
            return NormalizeAbsolute(right);
        if (string.IsNullOrWhiteSpace(right))
            return NormalizeAbsolute(left);

        if (left.EndsWith("/"))
            return left + right;
        return left + "/" + right;
    }

    private static string NormalizeAbsolute(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new Stack<string>();

        foreach (var part in parts)
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0)
                    stack.Pop();
                continue;
            }
            stack.Push(part);
        }

        var builder = new StringBuilder("/");
        var entries = stack.Reverse().ToArray();
        builder.Append(string.Join("/", entries));
        var normalized = builder.ToString();
        return string.IsNullOrEmpty(normalized) ? "/" : normalized;
    }
}
