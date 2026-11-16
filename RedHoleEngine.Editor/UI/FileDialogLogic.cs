namespace RedHoleEngine.Editor.UI;

/// <summary>
/// One entry of a file dialog filter, e.g. "Images (*.png;*.jpg)" with extensions [".png", ".jpg"].
/// An empty extension list matches every file.
/// </summary>
public sealed record FileFilter(string Description, IReadOnlyList<string> Extensions)
{
    /// <summary>Whether this filter accepts every file</summary>
    public bool MatchesAll => Extensions.Count == 0;
}

/// <summary>
/// UI-independent logic behind <see cref="FileDialog"/>: filter parsing, matching and path resolution.
/// This file is also compiled into RedHoleML.Trainer.
/// </summary>
public static class FileDialogLogic
{
    /// <summary>
    /// Parse a Windows-style filter string: "Description|*.a;*.b|Other|*.c".
    /// Patterns of "*" or "*.*" match all files. An empty string yields a single match-all filter.
    /// </summary>
    public static IReadOnlyList<FileFilter> ParseFilter(string? filter)
    {
        var result = new List<FileFilter>();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var parts = filter.Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                result.Add(new FileFilter(parts[i].Trim(), ParsePatterns(parts[i + 1])));
            }

            // A lone pattern with no description, e.g. "*.png;*.jpg"
            if (parts.Length == 1)
            {
                result.Add(new FileFilter(parts[0].Trim(), ParsePatterns(parts[0])));
            }
        }

        if (result.Count == 0)
        {
            result.Add(new FileFilter("All Files (*.*)", Array.Empty<string>()));
        }

        return result;
    }

    private static IReadOnlyList<string> ParsePatterns(string patterns)
    {
        var extensions = new List<string>();
        foreach (var raw in patterns.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw is "*" or "*.*")
                return Array.Empty<string>(); // match all

            var ext = raw.TrimStart('*');
            if (ext.Length == 0)
                continue;
            if (!ext.StartsWith('.'))
                ext = "." + ext;

            if (!extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                extensions.Add(ext);
        }
        return extensions;
    }

    /// <summary>
    /// Whether a file name is accepted by a filter (case-insensitive extension match)
    /// </summary>
    public static bool Matches(FileFilter filter, string fileName)
    {
        if (filter.MatchesAll)
            return true;

        foreach (var ext in filter.Extensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a file or folder name is hidden by convention (dot-prefixed)
    /// </summary>
    public static bool IsHidden(string name) => name.StartsWith('.');

    /// <summary>
    /// Combine the typed name with the current directory. Absolute input and "~" are honoured.
    /// </summary>
    public static string ResolveInput(string currentDirectory, string input)
    {
        input = input.Trim();
        if (input == "~" || input.StartsWith("~/") || input.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            input = input.Length <= 2 ? home : Path.Combine(home, input[2..]);
        }

        return Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(currentDirectory, input));
    }

    /// <summary>
    /// Apply the default extension to a save path when it has no extension accepted by the filter.
    /// </summary>
    public static string ApplyDefaultExtension(string path, string? defaultExtension, FileFilter? activeFilter)
    {
        if (string.IsNullOrEmpty(defaultExtension))
            return path;

        if (!defaultExtension.StartsWith('.'))
            defaultExtension = "." + defaultExtension;

        if (!Path.HasExtension(path))
            return path + defaultExtension;

        if (activeFilter != null && !activeFilter.MatchesAll && !Matches(activeFilter, path))
            return path + defaultExtension;

        return path;
    }

    /// <summary>
    /// Split a directory path into clickable breadcrumb segments (label, full path), root first.
    /// </summary>
    public static IReadOnlyList<(string Label, string Path)> GetBreadcrumbs(string directory)
    {
        var crumbs = new List<(string, string)>();
        if (string.IsNullOrEmpty(directory))
            return crumbs;

        var full = Path.GetFullPath(directory);
        var root = Path.GetPathRoot(full) ?? "";
        if (root.Length > 0)
        {
            crumbs.Add((root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is { Length: > 0 } r ? r : root, root));
        }

        var current = root;
        var rest = full[root.Length..];
        foreach (var part in rest.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            crumbs.Add((part, current));
        }

        return crumbs;
    }

    /// <summary>
    /// Walk up from a starting directory looking for a file or folder with the given name.
    /// Returns the full path, or null if none is found.
    /// </summary>
    public static string? FindInAncestors(string startDirectory, string name, int maxLevels = 8)
    {
        try
        {
            var dir = new DirectoryInfo(startDirectory);
            for (int i = 0; dir != null && i <= maxLevels; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, name);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                    return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Fall through
        }
        return null;
    }
}
