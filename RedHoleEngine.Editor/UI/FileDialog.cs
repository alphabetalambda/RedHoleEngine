using System.Numerics;
using ImGuiNET;

namespace RedHoleEngine.Editor.UI;

/// <summary>
/// Mode for the file dialog
/// </summary>
public enum FileDialogMode
{
    Open,
    Save
}

/// <summary>
/// Result of the file dialog
/// </summary>
public enum FileDialogResult
{
    None,
    Ok,
    Cancel
}

/// <summary>
/// Simple ImGui-based file browser dialog
/// </summary>
public class FileDialog
{
    private FileDialogMode _mode;
    private string _title = "File Dialog";
    private string _currentDirectory;
    private string _fileName = "";
    private string _filter = "*.*";
    private string _defaultExtension = "";
    private string[] _filterExtensions = Array.Empty<string>();
    
    private List<string> _directories = new();
    private List<string> _files = new();
    private int _selectedIndex = -1;
    private bool _isOpen;
    private bool _needsRefresh = true;
    
    /// <summary>
    /// Whether the dialog is currently open
    /// </summary>
    public bool IsOpen => _isOpen;
    
    /// <summary>
    /// The selected file path (valid after Ok result)
    /// </summary>
    public string SelectedPath { get; private set; } = "";

    public FileDialog()
    {
        _currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Open the dialog in the specified mode
    /// </summary>
    public void Open(FileDialogMode mode, string title, string filter = "All Files (*.*)|*.*", string defaultExtension = "")
    {
        _mode = mode;
        _title = title;
        _filter = filter;
        _defaultExtension = defaultExtension;
        _isOpen = true;
        _needsRefresh = true;
        _selectedIndex = -1;
        
        // Parse filter (format: "Description (*.ext)|*.ext")
        ParseFilter(filter);
        
        if (_mode == FileDialogMode.Save && !string.IsNullOrEmpty(_defaultExtension))
        {
            _fileName = "NewScene" + _defaultExtension;
        }
    }

    /// <summary>
    /// Open the dialog with a starting directory
    /// </summary>
    public void Open(FileDialogMode mode, string title, string startDirectory, string filter = "All Files (*.*)|*.*", string defaultExtension = "")
    {
        if (Directory.Exists(startDirectory))
        {
            _currentDirectory = startDirectory;
        }
        Open(mode, title, filter, defaultExtension);
    }

    private void ParseFilter(string filter)
    {
        // Parse filter like "Scene Files (*.rhes)|*.rhes|All Files (*.*)|*.*"
        var parts = filter.Split('|');
        var extensions = new List<string>();
        
        for (int i = 1; i < parts.Length; i += 2)
        {
            var ext = parts[i].Trim();
            if (ext != "*.*")
            {
                extensions.Add(ext.TrimStart('*'));
            }
        }
        
        _filterExtensions = extensions.ToArray();
    }

    /// <summary>
    /// Close the dialog
    /// </summary>
    public void Close()
    {
        _isOpen = false;
    }

    /// <summary>
    /// Draw the dialog and return the result
    /// </summary>
    public FileDialogResult Draw()
    {
        if (!_isOpen) return FileDialogResult.None;

        var result = FileDialogResult.None;
        
        ImGui.SetNextWindowSize(new Vector2(600, 450), ImGuiCond.FirstUseEver);
        
        var windowTitle = _mode == FileDialogMode.Open ? $"Open {_title}" : $"Save {_title}";
        
        bool open = true;
        if (ImGui.Begin(windowTitle + "###FileDialog", ref open, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
        {
            if (!open)
            {
                _isOpen = false;
                result = FileDialogResult.Cancel;
            }
            else
            {
                result = DrawContent();
            }
        }
        ImGui.End();

        return result;
    }

    private FileDialogResult DrawContent()
    {
        var result = FileDialogResult.None;
        
        // Current path display with navigation
        DrawPathBar();
        
        ImGui.Separator();
        
        // Refresh directory listing if needed
        if (_needsRefresh)
        {
            RefreshDirectory();
            _needsRefresh = false;
        }
        
        // File/folder list
        float listHeight = ImGui.GetContentRegionAvail().Y - 70;
        if (ImGui.BeginChild("FileList", new Vector2(0, listHeight), ImGuiChildFlags.Border))
        {
            DrawFileList();
        }
        ImGui.EndChild();
        
        ImGui.Separator();
        
        // File name input
        ImGui.Text("File name:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 160);
        if (ImGui.InputText("##FileName", ref _fileName, 256, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            result = TryConfirm();
        }
        
        // Buttons
        ImGui.SameLine();
        var buttonText = _mode == FileDialogMode.Open ? "Open" : "Save";
        if (ImGui.Button(buttonText, new Vector2(70, 0)))
        {
            result = TryConfirm();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(70, 0)))
        {
            _isOpen = false;
            result = FileDialogResult.Cancel;
        }
        
        return result;
    }

    private void DrawPathBar()
    {
        // Up button
        if (ImGui.Button("^"))
        {
            var parent = Directory.GetParent(_currentDirectory);
            if (parent != null)
            {
                _currentDirectory = parent.FullName;
                _needsRefresh = true;
                _selectedIndex = -1;
            }
        }
        
        ImGui.SameLine();
        
        // Path breadcrumbs
        var pathParts = _currentDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var builtPath = Path.DirectorySeparatorChar.ToString();
        
        // On Windows, handle drive letter
        if (OperatingSystem.IsWindows() && pathParts.Length > 0)
        {
            builtPath = pathParts[0] + Path.DirectorySeparatorChar;
            if (ImGui.SmallButton(pathParts[0]))
            {
                _currentDirectory = builtPath;
                _needsRefresh = true;
                _selectedIndex = -1;
            }
            pathParts = pathParts.Skip(1).ToArray();
        }
        else if (!OperatingSystem.IsWindows())
        {
            if (ImGui.SmallButton("/"))
            {
                _currentDirectory = "/";
                _needsRefresh = true;
                _selectedIndex = -1;
            }
        }
        
        foreach (var part in pathParts)
        {
            ImGui.SameLine(0, 2);
            ImGui.Text(">");
            ImGui.SameLine(0, 2);
            
            builtPath = Path.Combine(builtPath, part);
            var pathCopy = builtPath; // Capture for lambda
            
            if (ImGui.SmallButton(part))
            {
                _currentDirectory = pathCopy;
                _needsRefresh = true;
                _selectedIndex = -1;
            }
        }
    }

    private void DrawFileList()
    {
        int index = 0;
        
        // Draw directories first
        foreach (var dir in _directories)
        {
            var name = Path.GetFileName(dir);
            var displayName = "[DIR] " + name;
            
            bool isSelected = _selectedIndex == index;
            if (ImGui.Selectable(displayName, isSelected, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedIndex = index;
                
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _currentDirectory = dir;
                    _needsRefresh = true;
                    _selectedIndex = -1;
                }
            }
            index++;
        }
        
        // Draw files
        foreach (var file in _files)
        {
            var name = Path.GetFileName(file);
            
            bool isSelected = _selectedIndex == index;
            if (ImGui.Selectable(name, isSelected, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedIndex = index;
                _fileName = name;
                
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && _mode == FileDialogMode.Open)
                {
                    SelectedPath = file;
                    _isOpen = false;
                    return;
                }
            }
            index++;
        }
    }

    private void RefreshDirectory()
    {
        _directories.Clear();
        _files.Clear();
        
        try
        {
            // Get directories
            var dirs = Directory.GetDirectories(_currentDirectory)
                .Where(d => !Path.GetFileName(d).StartsWith('.')) // Hide hidden dirs
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            _directories.AddRange(dirs);
            
            // Get files with filter
            IEnumerable<string> files;
            if (_filterExtensions.Length == 0)
            {
                files = Directory.GetFiles(_currentDirectory);
            }
            else
            {
                files = Directory.GetFiles(_currentDirectory)
                    .Where(f => _filterExtensions.Any(ext => 
                        f.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || ext == ".*"));
            }
            
            _files.AddRange(files
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException)
        {
            // Can't access directory
        }
        catch (DirectoryNotFoundException)
        {
            // Directory doesn't exist, go to home
            _currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            RefreshDirectory();
        }
    }

    private FileDialogResult TryConfirm()
    {
        if (string.IsNullOrWhiteSpace(_fileName))
            return FileDialogResult.None;

        var path = Path.Combine(_currentDirectory, _fileName);
        
        // Add default extension if missing (for save mode)
        if (_mode == FileDialogMode.Save && !string.IsNullOrEmpty(_defaultExtension))
        {
            if (!Path.HasExtension(path) || 
                (_filterExtensions.Length > 0 && !_filterExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))))
            {
                path += _defaultExtension;
            }
        }
        
        if (_mode == FileDialogMode.Open)
        {
            if (!File.Exists(path))
            {
                // File doesn't exist
                return FileDialogResult.None;
            }
        }
        
        SelectedPath = path;
        _isOpen = false;
        return FileDialogResult.Ok;
    }
}
