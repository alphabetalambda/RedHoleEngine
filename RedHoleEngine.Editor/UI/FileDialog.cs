using System.Numerics;
using ImGuiNET;

namespace RedHoleEngine.Editor.UI;

/// <summary>
/// Mode for the file dialog
/// </summary>
public enum FileDialogMode
{
    /// <summary>Pick an existing file</summary>
    Open,
    /// <summary>Pick a file name to write (may not exist yet)</summary>
    Save,
    /// <summary>Pick a directory</summary>
    SelectFolder
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
/// Simple cross-platform ImGui file browser, shown as a modal popup.
/// Call <see cref="Open"/> once, then call
/// <see cref="Draw"/> every frame; it returns <see cref="FileDialogResult.Ok"/> on the frame the
/// user confirms. Because it is a popup, Draw may be called from inside another modal popup.
/// This file is also compiled into RedHoleML.Trainer.
/// </summary>
public class FileDialog
{
    private static int _nextInstanceId;

    private readonly int _instanceId = Interlocked.Increment(ref _nextInstanceId);
    private FileDialogMode _mode;
    private string _title = "File Dialog";
    private string _currentDirectory;
    private string _fileName = "";
    private string _defaultExtension = "";
    private IReadOnlyList<FileFilter> _filters = FileDialogLogic.ParseFilter(null);
    private int _activeFilter;
    private bool _showHidden;

    private readonly List<string> _directories = new();
    private readonly List<string> _files = new();
    private int _selectedIndex = -1;
    private bool _isOpen;
    private bool _openRequested;
    private bool _needsRefresh = true;
    private string _error = "";
    private string? _pendingOverwritePath;

    /// <summary>
    /// Whether the dialog is currently open
    /// </summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// The current mode
    /// </summary>
    public FileDialogMode Mode => _mode;

    /// <summary>
    /// The selected file or folder path (valid after an Ok result)
    /// </summary>
    public string SelectedPath { get; private set; } = "";

    /// <summary>
    /// Ask before overwriting an existing file in Save mode (default true)
    /// </summary>
    public bool ConfirmOverwrite { get; set; } = true;

    public FileDialog()
    {
        _currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Open the dialog in the specified mode
    /// </summary>
    /// <param name="mode">Open, Save or SelectFolder</param>
    /// <param name="title">Noun shown in the title, e.g. "Scene"</param>
    /// <param name="startDirectory">Folder to show first; ignored if it does not exist (the last folder is kept)</param>
    /// <param name="filter">Filter string: "Description|*.a;*.b|All Files|*.*"</param>
    /// <param name="defaultExtension">Extension appended in Save mode when missing</param>
    /// <param name="defaultFileName">Initial file name in Save mode</param>
    public void Open(FileDialogMode mode, string title, string startDirectory, string filter = "All Files (*.*)|*.*", string defaultExtension = "", string? defaultFileName = null)
    {
        if (!string.IsNullOrEmpty(startDirectory) && Directory.Exists(startDirectory))
        {
            _currentDirectory = Path.GetFullPath(startDirectory);
        }


        _mode = mode;
        _title = title;
        _defaultExtension = defaultExtension;
        _filters = FileDialogLogic.ParseFilter(filter);
        _activeFilter = 0;
        _isOpen = true;
        _openRequested = true;
        _needsRefresh = true;
        _selectedIndex = -1;
        _error = "";
        _pendingOverwritePath = null;
        SelectedPath = "";

        _fileName = mode switch
        {
            FileDialogMode.Save => defaultFileName ?? ("New" + title.Replace(" ", "") + defaultExtension),
            _ => ""
        };
    }

    /// <summary>
    /// Open a folder picker
    /// </summary>
    public void OpenFolder(string title, string? startDirectory = null)
    {
        Open(FileDialogMode.SelectFolder, title, startDirectory ?? "", "", "");
    }

    /// <summary>
    /// Close the dialog
    /// </summary>
    public void Close()
    {
        _isOpen = false;
        _openRequested = false;
    }

    private string PopupId => _mode switch
    {
        FileDialogMode.Open => $"Open {_title}",
        FileDialogMode.Save => $"Save {_title}",
        _ => $"Select {_title}"
    } + $"###FileDialog{_instanceId}";

    /// <summary>
    /// Draw the dialog and return the result
    /// </summary>
    public FileDialogResult Draw()
    {
        if (!_isOpen) return FileDialogResult.None;

        var popupId = PopupId;
        if (_openRequested)
        {
            ImGui.OpenPopup(popupId);
            _openRequested = false;
        }

        var result = FileDialogResult.None;

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(640, 460), ImGuiCond.Appearing);

        bool open = true;
        if (ImGui.BeginPopupModal(popupId, ref open, ImGuiWindowFlags.NoCollapse))
        {
            result = DrawContent();

            if (!open && result == FileDialogResult.None)
            {
                result = FileDialogResult.Cancel;
            }

            if (result != FileDialogResult.None)
            {
                _isOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
        else
        {
            // Popup was closed externally (e.g. its parent popup closed)
            _isOpen = false;
            result = FileDialogResult.Cancel;
        }

        return result;
    }

    private FileDialogResult DrawContent()
    {
        var result = FileDialogResult.None;

        DrawPathBar();
        ImGui.Separator();

        if (_needsRefresh)
        {
            RefreshDirectory();
            _needsRefresh = false;
        }

        // File/folder list
        float listHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2.5f;
        if (ImGui.BeginChild("FileList", new Vector2(0, MathF.Max(listHeight, 60f)), ImGuiChildFlags.Border))
        {
            result = DrawFileList();
        }
        ImGui.EndChild();

        if (!string.IsNullOrEmpty(_error))
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.4f, 1f), _error);
        }
        else if (_pendingOverwritePath != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f),
                $"\"{Path.GetFileName(_pendingOverwritePath)}\" exists. Click Save again to overwrite.");
        }
        else
        {
            ImGui.Spacing();
        }

        // Name input
        var label = _mode == FileDialogMode.SelectFolder ? "Folder:" : "File name:";
        ImGui.Text(label);
        ImGui.SameLine();

        float filterWidth = _mode == FileDialogMode.SelectFolder || _filters.Count <= 1 ? 0f : 170f;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 160 - filterWidth);
        if (ImGui.InputText("##FileName", ref _fileName, 512, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            result = TryConfirm();
        }
        else if (ImGui.IsItemEdited())
        {
            _pendingOverwritePath = null;
            _error = "";
        }

        if (filterWidth > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(filterWidth - 8);
            if (ImGui.BeginCombo("##Filter", _filters[_activeFilter].Description))
            {
                for (int i = 0; i < _filters.Count; i++)
                {
                    if (ImGui.Selectable(_filters[i].Description, i == _activeFilter))
                    {
                        _activeFilter = i;
                        _needsRefresh = true;
                    }
                }
                ImGui.EndCombo();
            }
        }

        // Buttons
        ImGui.SameLine();
        var buttonText = _mode switch
        {
            FileDialogMode.Open => "Open",
            FileDialogMode.Save => "Save",
            _ => "Select"
        };
        if (ImGui.Button(buttonText, new Vector2(70, 0)) && result == FileDialogResult.None)
        {
            result = TryConfirm();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(70, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            result = FileDialogResult.Cancel;
        }

        return result;
    }

    private void Navigate(string directory)
    {
        _currentDirectory = directory;
        _needsRefresh = true;
        _selectedIndex = -1;
        _error = "";
        _pendingOverwritePath = null;
        if (_mode == FileDialogMode.SelectFolder)
        {
            _fileName = "";
        }
    }

    private void DrawPathBar()
    {
        if (ImGui.Button("^"))
        {
            var parent = Directory.GetParent(_currentDirectory);
            if (parent != null)
            {
                Navigate(parent.FullName);
            }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Parent folder");

        ImGui.SameLine();
        if (ImGui.Button("~"))
        {
            Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Home folder");

        ImGui.SameLine();
        if (ImGui.Checkbox("Hidden", ref _showHidden))
        {
            _needsRefresh = true;
        }

        // Breadcrumbs
        var crumbs = FileDialogLogic.GetBreadcrumbs(_currentDirectory);
        for (int i = 0; i < crumbs.Count; i++)
        {
            ImGui.SameLine(0, i == 0 ? 8 : 2);
            if (i > 0)
            {
                ImGui.TextDisabled(">");
                ImGui.SameLine(0, 2);
            }

            ImGui.PushID(i);
            if (ImGui.SmallButton(crumbs[i].Label))
            {
                Navigate(crumbs[i].Path);
            }
            ImGui.PopID();
        }
    }

    private FileDialogResult DrawFileList()
    {
        int index = 0;

        foreach (var dir in _directories)
        {
            var name = Path.GetFileName(dir);
            bool isSelected = _selectedIndex == index;
            if (ImGui.Selectable("[DIR] " + name + "##d" + index, isSelected, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedIndex = index;
                if (_mode == FileDialogMode.SelectFolder)
                {
                    _fileName = name;
                }

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    Navigate(dir);
                    return FileDialogResult.None;
                }
            }
            index++;
        }

        if (_mode == FileDialogMode.SelectFolder)
        {
            // Show files for context, but they cannot be chosen
            foreach (var file in _files)
            {
                ImGui.TextDisabled("      " + Path.GetFileName(file));
            }
            return FileDialogResult.None;
        }

        foreach (var file in _files)
        {
            var name = Path.GetFileName(file);
            bool isSelected = _selectedIndex == index;
            if (ImGui.Selectable(name + "##f" + index, isSelected, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedIndex = index;
                _fileName = name;
                _pendingOverwritePath = null;
                _error = "";

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    return TryConfirm();
                }
            }
            index++;
        }

        return FileDialogResult.None;
    }

    private void RefreshDirectory()
    {
        _directories.Clear();
        _files.Clear();

        try
        {
            _directories.AddRange(Directory.GetDirectories(_currentDirectory)
                .Where(d => _showHidden || !FileDialogLogic.IsHidden(Path.GetFileName(d)))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase));

            var filter = _filters[Math.Clamp(_activeFilter, 0, _filters.Count - 1)];
            _files.AddRange(Directory.GetFiles(_currentDirectory)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return (_showHidden || !FileDialogLogic.IsHidden(name)) &&
                           (_mode == FileDialogMode.SelectFolder || FileDialogLogic.Matches(filter, name));
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException)
        {
            _error = "Access denied.";
        }
        catch (DirectoryNotFoundException)
        {
            _currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            RefreshDirectory();
        }
        catch (IOException ex)
        {
            _error = ex.Message;
        }
    }

    private FileDialogResult TryConfirm()
    {
        _error = "";

        if (_mode == FileDialogMode.SelectFolder)
        {
            var folder = string.IsNullOrWhiteSpace(_fileName)
                ? _currentDirectory
                : FileDialogLogic.ResolveInput(_currentDirectory, _fileName);

            if (!Directory.Exists(folder))
            {
                _error = "Folder does not exist.";
                return FileDialogResult.None;
            }

            SelectedPath = folder;
            return FileDialogResult.Ok;
        }

        if (string.IsNullOrWhiteSpace(_fileName))
            return FileDialogResult.None;

        string path;
        try
        {
            path = FileDialogLogic.ResolveInput(_currentDirectory, _fileName);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _error = "Invalid path.";
            return FileDialogResult.None;
        }

        // Typing a folder path navigates into it
        if (Directory.Exists(path))
        {
            Navigate(path);
            _fileName = "";
            return FileDialogResult.None;
        }

        if (_mode == FileDialogMode.Save)
        {
            path = FileDialogLogic.ApplyDefaultExtension(path, _defaultExtension, _filters[_activeFilter]);

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                _error = "Folder does not exist.";
                return FileDialogResult.None;
            }

            if (ConfirmOverwrite && File.Exists(path) && _pendingOverwritePath != path)
            {
                _pendingOverwritePath = path;
                return FileDialogResult.None;
            }
        }
        else if (!File.Exists(path))
        {
            _error = "File not found.";
            return FileDialogResult.None;
        }

        _pendingOverwritePath = null;
        SelectedPath = path;
        return FileDialogResult.Ok;
    }
}
