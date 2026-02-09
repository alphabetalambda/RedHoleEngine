using System.Numerics;
using ImGuiNET;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Asset type categories for display
/// </summary>
public enum AssetType
{
    Unknown,
    Folder,
    Scene,
    Texture,
    Mesh,
    Material,
    Script,
    Audio,
    Shader,
    Prefab,
    Config
}

/// <summary>
/// Represents an asset entry in the browser
/// </summary>
public class AssetEntry
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public AssetType Type { get; set; }
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Panel for browsing and managing project assets
/// </summary>
public class AssetBrowserPanel : EditorPanel
{
    private readonly Func<string> _getProjectPath;
    private readonly Action<string> _openSceneAction;
    
    private string _currentPath = "";
    private string _rootPath = "";
    private List<AssetEntry> _entries = new();
    private AssetEntry? _selectedEntry;
    private string _searchFilter = "";
    private bool _needsRefresh = true;
    private float _iconSize = 64f;
    private bool _showGridView = true;
    
    // Rename state
    private bool _isRenaming;
    private string _renameBuffer = "";
    private AssetEntry? _renamingEntry;
    
    // Context menu
    private bool _showContextMenu;
    private Vector2 _contextMenuPos;

    public override string Title => "Assets";

    public AssetBrowserPanel(Func<string> getProjectPath, Action<string> openSceneAction)
    {
        _getProjectPath = getProjectPath;
        _openSceneAction = openSceneAction;
    }

    protected override void OnDraw()
    {
        // Update root path if project changed
        var projectPath = _getProjectPath();
        if (!string.IsNullOrEmpty(projectPath))
        {
            var newRoot = Directory.Exists(projectPath) 
                ? projectPath 
                : Path.GetDirectoryName(projectPath) ?? "";
            
            if (newRoot != _rootPath)
            {
                _rootPath = newRoot;
                _currentPath = _rootPath;
                _needsRefresh = true;
            }
        }

        if (string.IsNullOrEmpty(_rootPath) || !Directory.Exists(_rootPath))
        {
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), "No project loaded");
            ImGui.TextWrapped("Load a game project to browse assets.");
            return;
        }

        DrawToolbar();
        ImGui.Separator();
        DrawBreadcrumbs();
        ImGui.Separator();
        
        if (_needsRefresh)
        {
            RefreshDirectory();
            _needsRefresh = false;
        }

        if (_showGridView)
            DrawGridView();
        else
            DrawListView();

        HandleContextMenu();
    }

    private void DrawToolbar()
    {
        // Search filter
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputTextWithHint("##Search", "Search...", ref _searchFilter, 256))
        {
            _needsRefresh = true;
        }

        ImGui.SameLine();

        // View toggle
        if (ImGui.Button(_showGridView ? "List" : "Grid"))
        {
            _showGridView = !_showGridView;
        }
        if (ImGui.IsItemHovered()) 
            ImGui.SetTooltip(_showGridView ? "Switch to list view" : "Switch to grid view");

        ImGui.SameLine();

        // Icon size slider (grid view only)
        if (_showGridView)
        {
            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("##IconSize", ref _iconSize, 32f, 128f, "%.0f");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Icon size");
        }

        ImGui.SameLine();

        // Refresh button
        if (ImGui.Button("Refresh"))
        {
            _needsRefresh = true;
        }

        ImGui.SameLine();

        // Create folder button
        if (ImGui.Button("+ Folder"))
        {
            CreateNewFolder();
        }
    }

    private void DrawBreadcrumbs()
    {
        // Root button
        if (ImGui.SmallButton("Project"))
        {
            _currentPath = _rootPath;
            _needsRefresh = true;
        }

        // Build path parts relative to root
        if (_currentPath != _rootPath && _currentPath.StartsWith(_rootPath))
        {
            var relativePath = _currentPath.Substring(_rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            
            var builtPath = _rootPath;
            foreach (var part in parts)
            {
                ImGui.SameLine(0, 2);
                ImGui.TextDisabled(">");
                ImGui.SameLine(0, 2);
                
                builtPath = Path.Combine(builtPath, part);
                var pathCopy = builtPath;
                
                if (ImGui.SmallButton(part))
                {
                    _currentPath = pathCopy;
                    _needsRefresh = true;
                }
            }
        }
    }

    private void DrawGridView()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)(availWidth / (_iconSize + 16)));
        
        if (ImGui.BeginChild("AssetGrid", Vector2.Zero, ImGuiChildFlags.None))
        {
            int column = 0;
            
            foreach (var entry in _entries)
            {
                if (column > 0)
                    ImGui.SameLine();

                DrawAssetCard(entry);
                
                column++;
                if (column >= columns)
                    column = 0;
            }
        }
        ImGui.EndChild();
    }

    private void DrawListView()
    {
        if (ImGui.BeginChild("AssetList", Vector2.Zero, ImGuiChildFlags.None))
        {
            // Header
            ImGui.Columns(4, "AssetColumns", true);
            ImGui.SetColumnWidth(0, 250);
            ImGui.SetColumnWidth(1, 80);
            ImGui.SetColumnWidth(2, 100);
            
            ImGui.Text("Name"); ImGui.NextColumn();
            ImGui.Text("Type"); ImGui.NextColumn();
            ImGui.Text("Size"); ImGui.NextColumn();
            ImGui.Text("Modified"); ImGui.NextColumn();
            ImGui.Separator();

            foreach (var entry in _entries)
            {
                DrawAssetRow(entry);
            }
            
            ImGui.Columns(1);
        }
        ImGui.EndChild();
    }

    private void DrawAssetCard(AssetEntry entry)
    {
        ImGui.PushID(entry.FullPath);
        
        var isSelected = _selectedEntry == entry;
        var cardSize = new Vector2(_iconSize + 8, _iconSize + 32);
        
        // Card background
        var cursorPos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        
        if (isSelected)
        {
            drawList.AddRectFilled(cursorPos, cursorPos + cardSize, 
                ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.7f, 0.5f)), 4f);
        }

        // Invisible button for interaction
        if (ImGui.InvisibleButton("##Card", cardSize))
        {
            _selectedEntry = entry;
        }

        // Handle double-click
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            HandleDoubleClick(entry);
        }

        // Context menu
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _selectedEntry = entry;
            _showContextMenu = true;
            _contextMenuPos = ImGui.GetMousePos();
        }

        // Draw icon
        var iconPos = cursorPos + new Vector2(4, 4);
        var iconColor = GetAssetColor(entry.Type);
        var iconChar = GetAssetIcon(entry.Type);
        
        // Icon background
        drawList.AddRectFilled(iconPos, iconPos + new Vector2(_iconSize, _iconSize),
            ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.18f, 1f)), 4f);
        
        // Icon text (centered)
        var iconTextSize = ImGui.CalcTextSize(iconChar);
        var iconTextPos = iconPos + new Vector2((_iconSize - iconTextSize.X) / 2, (_iconSize - iconTextSize.Y) / 2);
        drawList.AddText(iconTextPos, ImGui.GetColorU32(iconColor), iconChar);

        // Draw name (truncated)
        var namePos = cursorPos + new Vector2(4, _iconSize + 8);
        var maxNameWidth = _iconSize;
        var name = entry.Name;
        
        // Handle rename
        if (_isRenaming && _renamingEntry == entry)
        {
            ImGui.SetCursorScreenPos(namePos);
            ImGui.SetNextItemWidth(maxNameWidth);
            ImGui.SetKeyboardFocusHere();
            
            if (ImGui.InputText("##Rename", ref _renameBuffer, 256, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                FinishRename();
            }
            if (!ImGui.IsItemActive() && !ImGui.IsItemHovered())
            {
                CancelRename();
            }
        }
        else
        {
            // Truncate name if too long
            while (ImGui.CalcTextSize(name).X > maxNameWidth && name.Length > 3)
            {
                name = name.Substring(0, name.Length - 4) + "...";
            }
            drawList.AddText(namePos, ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f)), name);
        }

        // Tooltip with full name
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(entry.Name);
            if (!entry.IsDirectory)
            {
                ImGui.TextDisabled($"{FormatFileSize(entry.Size)}");
            }
            ImGui.EndTooltip();
        }

        ImGui.PopID();
    }

    private void DrawAssetRow(AssetEntry entry)
    {
        ImGui.PushID(entry.FullPath);
        
        var isSelected = _selectedEntry == entry;
        
        // Selectable row
        if (ImGui.Selectable($"{GetAssetIcon(entry.Type)} {entry.Name}", isSelected, 
            ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
        {
            _selectedEntry = entry;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            HandleDoubleClick(entry);
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _selectedEntry = entry;
            _showContextMenu = true;
            _contextMenuPos = ImGui.GetMousePos();
        }

        ImGui.NextColumn();
        ImGui.TextDisabled(entry.Type.ToString());
        ImGui.NextColumn();
        ImGui.TextDisabled(entry.IsDirectory ? "-" : FormatFileSize(entry.Size));
        ImGui.NextColumn();
        ImGui.TextDisabled(entry.LastModified.ToString("yyyy-MM-dd HH:mm"));
        ImGui.NextColumn();

        ImGui.PopID();
    }

    private void HandleDoubleClick(AssetEntry entry)
    {
        if (entry.IsDirectory)
        {
            _currentPath = entry.FullPath;
            _needsRefresh = true;
        }
        else if (entry.Type == AssetType.Scene)
        {
            _openSceneAction(entry.FullPath);
        }
    }

    private void HandleContextMenu()
    {
        if (_showContextMenu)
        {
            ImGui.SetNextWindowPos(_contextMenuPos);
            ImGui.OpenPopup("AssetContextMenu");
            _showContextMenu = false;
        }

        if (ImGui.BeginPopup("AssetContextMenu"))
        {
            if (_selectedEntry != null)
            {
                if (_selectedEntry.Type == AssetType.Scene)
                {
                    if (ImGui.MenuItem("Open Scene"))
                    {
                        _openSceneAction(_selectedEntry.FullPath);
                    }
                    ImGui.Separator();
                }

                if (ImGui.MenuItem("Rename", "F2"))
                {
                    StartRename(_selectedEntry);
                }

                if (ImGui.MenuItem("Delete", "Delete"))
                {
                    DeleteAsset(_selectedEntry);
                }

                if (_selectedEntry.IsDirectory)
                {
                    ImGui.Separator();
                    if (ImGui.MenuItem("Open in Explorer"))
                    {
                        OpenInExplorer(_selectedEntry.FullPath);
                    }
                }
            }

            ImGui.Separator();

            if (ImGui.MenuItem("New Folder"))
            {
                CreateNewFolder();
            }

            if (ImGui.MenuItem("Refresh"))
            {
                _needsRefresh = true;
            }

            ImGui.EndPopup();
        }
    }

    private void RefreshDirectory()
    {
        _entries.Clear();
        
        if (!Directory.Exists(_currentPath))
        {
            _currentPath = _rootPath;
            if (!Directory.Exists(_currentPath))
                return;
        }

        try
        {
            // Add parent directory link if not at root
            if (_currentPath != _rootPath)
            {
                var parent = Directory.GetParent(_currentPath);
                if (parent != null && parent.FullName.StartsWith(_rootPath))
                {
                    _entries.Add(new AssetEntry
                    {
                        Name = "..",
                        FullPath = parent.FullName,
                        Type = AssetType.Folder,
                        IsDirectory = true
                    });
                }
            }

            // Get directories
            var dirs = Directory.GetDirectories(_currentPath)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);

            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (MatchesSearch(name))
                {
                    _entries.Add(new AssetEntry
                    {
                        Name = name,
                        FullPath = dir,
                        Type = AssetType.Folder,
                        IsDirectory = true,
                        LastModified = Directory.GetLastWriteTime(dir)
                    });
                }
            }

            // Get files
            var files = Directory.GetFiles(_currentPath)
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (MatchesSearch(name))
                {
                    var info = new FileInfo(file);
                    _entries.Add(new AssetEntry
                    {
                        Name = name,
                        FullPath = file,
                        Type = DetectAssetType(file),
                        IsDirectory = false,
                        Size = info.Length,
                        LastModified = info.LastWriteTime
                    });
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Access denied
        }
    }

    private bool MatchesSearch(string name)
    {
        if (string.IsNullOrEmpty(_searchFilter))
            return true;
        return name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static AssetType DetectAssetType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".rhes" => AssetType.Scene,
            ".scene" => AssetType.Scene,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".hdr" => AssetType.Texture,
            ".obj" or ".fbx" or ".gltf" or ".glb" => AssetType.Mesh,
            ".mat" or ".material" => AssetType.Material,
            ".cs" => AssetType.Script,
            ".wav" or ".mp3" or ".ogg" or ".flac" => AssetType.Audio,
            ".glsl" or ".hlsl" or ".vert" or ".frag" or ".comp" or ".spv" => AssetType.Shader,
            ".prefab" => AssetType.Prefab,
            ".json" or ".xml" or ".yaml" or ".yml" or ".config" => AssetType.Config,
            _ => AssetType.Unknown
        };
    }

    private static string GetAssetIcon(AssetType type)
    {
        return type switch
        {
            AssetType.Folder => "[D]",
            AssetType.Scene => "[S]",
            AssetType.Texture => "[T]",
            AssetType.Mesh => "[M]",
            AssetType.Material => "[Mt]",
            AssetType.Script => "[C#]",
            AssetType.Audio => "[A]",
            AssetType.Shader => "[Sh]",
            AssetType.Prefab => "[P]",
            AssetType.Config => "[Cfg]",
            _ => "[?]"
        };
    }

    private static Vector4 GetAssetColor(AssetType type)
    {
        return type switch
        {
            AssetType.Folder => new Vector4(0.9f, 0.8f, 0.3f, 1f),
            AssetType.Scene => new Vector4(0.3f, 0.8f, 0.4f, 1f),
            AssetType.Texture => new Vector4(0.8f, 0.5f, 0.9f, 1f),
            AssetType.Mesh => new Vector4(0.4f, 0.7f, 0.9f, 1f),
            AssetType.Material => new Vector4(0.9f, 0.6f, 0.4f, 1f),
            AssetType.Script => new Vector4(0.4f, 0.9f, 0.7f, 1f),
            AssetType.Audio => new Vector4(0.9f, 0.4f, 0.6f, 1f),
            AssetType.Shader => new Vector4(0.7f, 0.7f, 0.9f, 1f),
            AssetType.Prefab => new Vector4(0.5f, 0.8f, 0.8f, 1f),
            AssetType.Config => new Vector4(0.6f, 0.6f, 0.6f, 1f),
            _ => new Vector4(0.5f, 0.5f, 0.5f, 1f)
        };
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.#} {sizes[order]}";
    }

    private void CreateNewFolder()
    {
        var baseName = "New Folder";
        var newPath = Path.Combine(_currentPath, baseName);
        int counter = 1;
        
        while (Directory.Exists(newPath))
        {
            newPath = Path.Combine(_currentPath, $"{baseName} {counter}");
            counter++;
        }

        try
        {
            Directory.CreateDirectory(newPath);
            _needsRefresh = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create folder: {ex.Message}");
        }
    }

    private void StartRename(AssetEntry entry)
    {
        _isRenaming = true;
        _renamingEntry = entry;
        _renameBuffer = entry.Name;
    }

    private void FinishRename()
    {
        if (_renamingEntry == null || string.IsNullOrWhiteSpace(_renameBuffer))
        {
            CancelRename();
            return;
        }

        var newPath = Path.Combine(Path.GetDirectoryName(_renamingEntry.FullPath)!, _renameBuffer);
        
        if (newPath == _renamingEntry.FullPath)
        {
            CancelRename();
            return;
        }

        try
        {
            if (_renamingEntry.IsDirectory)
            {
                Directory.Move(_renamingEntry.FullPath, newPath);
            }
            else
            {
                File.Move(_renamingEntry.FullPath, newPath);
            }
            _needsRefresh = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to rename: {ex.Message}");
        }

        CancelRename();
    }

    private void CancelRename()
    {
        _isRenaming = false;
        _renamingEntry = null;
        _renameBuffer = "";
    }

    private void DeleteAsset(AssetEntry entry)
    {
        try
        {
            if (entry.IsDirectory)
            {
                Directory.Delete(entry.FullPath, true);
            }
            else
            {
                File.Delete(entry.FullPath);
            }
            
            if (_selectedEntry == entry)
                _selectedEntry = null;
            
            _needsRefresh = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete: {ex.Message}");
        }
    }

    private void OpenInExplorer(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", path);
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", path);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open explorer: {ex.Message}");
        }
    }
}
