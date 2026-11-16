using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace RedHoleEngine.Editor.UI;

/// <summary>
/// UI-independent helpers for About dialogs and opening documentation.
/// This file is also compiled into RedHoleML.Trainer.
/// </summary>
public static class AppInfo
{
    /// <summary>Public source repository</summary>
    public const string RepositoryUrl = "https://github.com/alphabetalambda/RedHoleEngine";

    /// <summary>
    /// Human-readable version of an assembly. Uses the informational version when present,
    /// shortening any "+commit" build metadata to seven characters.
    /// </summary>
    public static string GetVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return FormatInformationalVersion(informational);

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Format "1.2.3+0123456789abcdef" as "1.2.3 (0123456)"
    /// </summary>
    public static string FormatInformationalVersion(string informational)
    {
        var plus = informational.IndexOf('+');
        if (plus < 0)
            return informational;

        var version = informational[..plus];
        var metadata = informational[(plus + 1)..];
        if (metadata.Length == 0)
            return version;

        return $"{version} ({(metadata.Length > 7 ? metadata[..7] : metadata)})";
    }

    /// <summary>
    /// Locate a repository file (README.md, LICENSE, docs) next to or above the running executable,
    /// falling back to the current directory.
    /// </summary>
    public static string? FindRepositoryFile(string name)
    {
        return FileDialogLogic.FindInAncestors(AppContext.BaseDirectory, name)
               ?? FileDialogLogic.FindInAncestors(Directory.GetCurrentDirectory(), name);
    }

    /// <summary>
    /// The license title: the first <paramref name="lines"/> non-empty lines of a license file, joined with ", ".
    /// Returns null if the file cannot be read.
    /// </summary>
    public static string? ReadLicenseSummary(string? licensePath, int lines = 2)
    {
        if (string.IsNullOrEmpty(licensePath) || !File.Exists(licensePath))
            return null;

        try
        {
            var head = File.ReadLines(licensePath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Take(lines)
                .ToList();
            return head.Count > 0 ? string.Join(", ", head) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Open a URL, file or folder with the operating system's default handler.
    /// </summary>
    /// <returns>False with an error message if nothing could be launched</returns>
    public static bool TryOpenWithShell(string target, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "Nothing to open.";
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException or FileNotFoundException)
        {
            error = $"Could not open {target}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Open the local documentation: the docs folder, else README.md, else the online repository.
    /// </summary>
    /// <returns>What was opened, or an error message on failure</returns>
    public static bool TryOpenDocumentation(out string message, string? preferredDocument = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(preferredDocument))
        {
            var preferred = FindRepositoryFile(preferredDocument);
            if (preferred != null) candidates.Add(preferred);
        }
        var readme = FindRepositoryFile("README.md");
        if (readme != null) candidates.Add(readme);
        var docs = FindRepositoryFile("docs");
        if (docs != null) candidates.Add(docs);
        candidates.Add(RepositoryUrl);

        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            if (TryOpenWithShell(candidate, out var error))
            {
                message = $"Opened {candidate}";
                return true;
            }
            errors.Add(error);
        }

        message = string.Join(Environment.NewLine, errors);
        return false;
    }
}
