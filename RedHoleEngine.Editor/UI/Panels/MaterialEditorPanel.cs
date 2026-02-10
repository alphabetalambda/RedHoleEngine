using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Editor.Project;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Panel for editing PBR materials
/// </summary>
public class MaterialEditorPanel : EditorPanel
{
    private readonly ProjectManager _projectManager;
    private readonly MaterialLibrary _materialLibrary;
    private readonly Action<string>? _onMaterialSelected;
    
    private PbrMaterial? _currentMaterial;
    private string _currentFilePath = string.Empty;
    private bool _isDirty;
    private string _searchFilter = string.Empty;
    private int _selectedPreset = -1;
    
    // Temporary editing values
    private Vector4 _baseColor;
    private float _metallic;
    private float _roughness;
    private Vector3 _emissiveColor;
    private float _emissiveIntensity;
    private float _ior;
    private float _clearcoat;
    private float _clearcoatRoughness;
    private int _alphaMode;
    private float _alphaCutoff;
    private bool _doubleSided;
    
    private static readonly string[] AlphaModeNames = { "Opaque", "Mask", "Blend" };
    private static readonly string[] PresetNames = 
    {
        "Default", "Gold", "Copper", "Iron", "Chrome", "Aluminum",
        "Red Plastic", "Blue Plastic", "White Plastic",
        "Glass", "Emissive", "Car Paint"
    };
    
    public override string Title => "Material Editor";
    
    public MaterialEditorPanel(ProjectManager projectManager, MaterialLibrary materialLibrary, Action<string>? onMaterialSelected = null)
    {
        _projectManager = projectManager;
        _materialLibrary = materialLibrary;
        _onMaterialSelected = onMaterialSelected;
        
        // Start with a new default material
        NewMaterial();
    }
    
    /// <summary>
    /// The currently edited material
    /// </summary>
    public PbrMaterial? CurrentMaterial => _currentMaterial;
    
    /// <summary>
    /// Whether there are unsaved changes
    /// </summary>
    public bool IsDirty => _isDirty;
    
    /// <summary>
    /// Create a new material
    /// </summary>
    public void NewMaterial()
    {
        _currentMaterial = PbrMaterial.Default();
        _currentFilePath = string.Empty;
        _isDirty = false;
        SyncFromMaterial();
    }
    
    /// <summary>
    /// Load a material from file
    /// </summary>
    public bool LoadMaterial(string filePath)
    {
        if (MaterialSerializer.TryLoadFromFile(filePath, out var material, out var error))
        {
            _currentMaterial = material;
            _currentFilePath = filePath;
            _isDirty = false;
            SyncFromMaterial();
            _onMaterialSelected?.Invoke(filePath);
            return true;
        }
        
        Console.WriteLine($"Failed to load material: {error}");
        return false;
    }
    
    /// <summary>
    /// Save the current material
    /// </summary>
    public bool SaveMaterial(string? filePath = null)
    {
        if (_currentMaterial == null) return false;
        
        SyncToMaterial();
        
        var path = filePath ?? _currentFilePath;
        if (string.IsNullOrEmpty(path))
        {
            // Need to pick a path
            return false;
        }
        
        try
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            MaterialSerializer.SaveToFile(_currentMaterial, path);
            _currentFilePath = path;
            _isDirty = false;
            Console.WriteLine($"Material saved: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save material: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Apply a preset to the current material
    /// </summary>
    public void ApplyPreset(int presetIndex)
    {
        _currentMaterial = presetIndex switch
        {
            0 => PbrMaterial.Default(),
            1 => PbrMaterial.Gold(),
            2 => PbrMaterial.Copper(),
            3 => PbrMaterial.Iron(),
            4 => PbrMaterial.Metal(new Vector3(0.95f, 0.95f, 0.95f), 0.1f),
            5 => PbrMaterial.Metal(new Vector3(0.91f, 0.92f, 0.92f), 0.2f),
            6 => PbrMaterial.Plastic(new Vector3(0.8f, 0.1f, 0.1f)),
            7 => PbrMaterial.Plastic(new Vector3(0.1f, 0.1f, 0.8f)),
            8 => PbrMaterial.Plastic(new Vector3(0.9f, 0.9f, 0.9f)),
            9 => PbrMaterial.Glass(),
            10 => PbrMaterial.Emissive(new Vector3(1f, 0.5f, 0.2f), 5f),
            11 => PbrMaterial.CarPaint(new Vector3(0.7f, 0.1f, 0.1f)),
            _ => PbrMaterial.Default()
        };
        
        _currentMaterial.Name = PresetNames[presetIndex];
        SyncFromMaterial();
        _isDirty = true;
    }
    
    private void SyncFromMaterial()
    {
        if (_currentMaterial == null) return;
        
        _baseColor = _currentMaterial.BaseColorFactor;
        _metallic = _currentMaterial.MetallicFactor;
        _roughness = _currentMaterial.RoughnessFactor;
        _emissiveColor = _currentMaterial.EmissiveFactor;
        _emissiveIntensity = _currentMaterial.EmissiveIntensity;
        _ior = _currentMaterial.IndexOfRefraction;
        _clearcoat = _currentMaterial.ClearcoatFactor;
        _clearcoatRoughness = _currentMaterial.ClearcoatRoughness;
        _alphaMode = (int)_currentMaterial.AlphaMode;
        _alphaCutoff = _currentMaterial.AlphaCutoff;
        _doubleSided = _currentMaterial.DoubleSided;
    }
    
    private void SyncToMaterial()
    {
        if (_currentMaterial == null) return;
        
        _currentMaterial.BaseColorFactor = _baseColor;
        _currentMaterial.MetallicFactor = _metallic;
        _currentMaterial.RoughnessFactor = _roughness;
        _currentMaterial.EmissiveFactor = _emissiveColor;
        _currentMaterial.EmissiveIntensity = _emissiveIntensity;
        _currentMaterial.IndexOfRefraction = _ior;
        _currentMaterial.ClearcoatFactor = _clearcoat;
        _currentMaterial.ClearcoatRoughness = _clearcoatRoughness;
        _currentMaterial.AlphaMode = (AlphaMode)_alphaMode;
        _currentMaterial.AlphaCutoff = _alphaCutoff;
        _currentMaterial.DoubleSided = _doubleSided;
    }

    protected override void OnDraw()
    {
        if (_currentMaterial == null)
        {
            ImGui.TextDisabled("No material loaded");
            if (ImGui.Button("New Material"))
            {
                NewMaterial();
            }
            return;
        }
        
        // Toolbar
        DrawToolbar();
        
        ImGui.Separator();
        
        // Material name
        var name = _currentMaterial.Name;
        if (ImGui.InputText("Name", ref name, 256))
        {
            _currentMaterial.Name = name;
            _isDirty = true;
        }
        
        // File path (read-only)
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            ImGui.TextDisabled($"File: {Path.GetFileName(_currentFilePath)}");
        }
        else
        {
            ImGui.TextDisabled("File: (unsaved)");
        }
        
        ImGui.Separator();
        
        // Presets
        if (ImGui.CollapsingHeader("Presets"))
        {
            ImGui.PushItemWidth(-1);
            if (ImGui.Combo("##Preset", ref _selectedPreset, PresetNames, PresetNames.Length))
            {
                if (_selectedPreset >= 0)
                {
                    ApplyPreset(_selectedPreset);
                }
            }
            ImGui.PopItemWidth();
        }
        
        // Base Color
        if (ImGui.CollapsingHeader("Base Color", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.ColorEdit4("Color##Base", ref _baseColor))
            {
                _isDirty = true;
            }
            
            // Texture slot (placeholder)
            ImGui.TextDisabled("Texture: (none)");
            if (ImGui.Button("Browse...##BaseTexture"))
            {
                // TODO: Open file dialog for texture
            }
        }
        
        // Metallic-Roughness
        if (ImGui.CollapsingHeader("Metallic-Roughness", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.SliderFloat("Metallic", ref _metallic, 0f, 1f))
            {
                _isDirty = true;
            }
            
            if (ImGui.SliderFloat("Roughness", ref _roughness, 0f, 1f))
            {
                _isDirty = true;
            }
            
            // Quick buttons for common values
            ImGui.Text("Quick:");
            ImGui.SameLine();
            if (ImGui.SmallButton("Dielectric"))
            {
                _metallic = 0f;
                _roughness = 0.5f;
                _isDirty = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Metal"))
            {
                _metallic = 1f;
                _roughness = 0.3f;
                _isDirty = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Mirror"))
            {
                _metallic = 1f;
                _roughness = 0f;
                _isDirty = true;
            }
        }
        
        // Emission
        if (ImGui.CollapsingHeader("Emission"))
        {
            if (ImGui.ColorEdit3("Color##Emissive", ref _emissiveColor))
            {
                _isDirty = true;
            }
            
            if (ImGui.SliderFloat("Intensity##Emissive", ref _emissiveIntensity, 0f, 20f))
            {
                _isDirty = true;
            }
            
            // Quick buttons
            ImGui.Text("Quick:");
            ImGui.SameLine();
            if (ImGui.SmallButton("Off"))
            {
                _emissiveColor = Vector3.Zero;
                _emissiveIntensity = 1f;
                _isDirty = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Glow"))
            {
                _emissiveIntensity = 5f;
                _isDirty = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Bright"))
            {
                _emissiveIntensity = 15f;
                _isDirty = true;
            }
        }
        
        // Advanced
        if (ImGui.CollapsingHeader("Advanced"))
        {
            if (ImGui.SliderFloat("IOR", ref _ior, 1f, 3f, "%.2f"))
            {
                _isDirty = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Index of Refraction\n1.0 = Air\n1.33 = Water\n1.5 = Glass\n2.4 = Diamond");
            }
            
            if (ImGui.Combo("Alpha Mode", ref _alphaMode, AlphaModeNames, AlphaModeNames.Length))
            {
                _isDirty = true;
            }
            
            if (_alphaMode == 1) // Mask
            {
                if (ImGui.SliderFloat("Alpha Cutoff", ref _alphaCutoff, 0f, 1f))
                {
                    _isDirty = true;
                }
            }
            
            if (ImGui.Checkbox("Double Sided", ref _doubleSided))
            {
                _isDirty = true;
            }
        }
        
        // Clearcoat
        if (ImGui.CollapsingHeader("Clearcoat"))
        {
            if (ImGui.SliderFloat("Clearcoat", ref _clearcoat, 0f, 1f))
            {
                _isDirty = true;
            }
            
            if (_clearcoat > 0f)
            {
                if (ImGui.SliderFloat("Clearcoat Roughness", ref _clearcoatRoughness, 0f, 1f))
                {
                    _isDirty = true;
                }
            }
            
            ImGui.TextDisabled("Use for car paint, lacquered wood, etc.");
        }
        
        // Preview (placeholder)
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Preview"))
        {
            ImGui.TextDisabled("Material preview coming soon...");
            
            // Show a summary
            ImGui.Text($"Type: {(_metallic > 0.5f ? "Metal" : "Dielectric")}");
            ImGui.Text($"Roughness: {(_roughness < 0.3f ? "Smooth" : _roughness > 0.7f ? "Rough" : "Medium")}");
            if (_emissiveIntensity > 0f && (_emissiveColor.X > 0f || _emissiveColor.Y > 0f || _emissiveColor.Z > 0f))
            {
                ImGui.Text("Emissive: Yes");
            }
            if (_clearcoat > 0f)
            {
                ImGui.Text("Clearcoat: Yes");
            }
        }
    }
    
    private void DrawToolbar()
    {
        if (ImGui.Button("New"))
        {
            NewMaterial();
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Open"))
        {
            // TODO: Open file dialog
            var materialsPath = GetMaterialsPath();
            if (!string.IsNullOrEmpty(materialsPath))
            {
                // For now, just list available materials
                Console.WriteLine($"Materials folder: {materialsPath}");
            }
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Save"))
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                // Need Save As
                SaveMaterialAs();
            }
            else
            {
                SaveMaterial();
            }
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Save As"))
        {
            SaveMaterialAs();
        }
        
        // Dirty indicator
        if (_isDirty)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "*");
        }
    }
    
    private void SaveMaterialAs()
    {
        if (_currentMaterial == null) return;
        
        SyncToMaterial();
        
        var materialsPath = GetMaterialsPath();
        if (string.IsNullOrEmpty(materialsPath))
        {
            Console.WriteLine("No project loaded - cannot determine materials path");
            return;
        }
        
        // Ensure directory exists
        if (!Directory.Exists(materialsPath))
        {
            Directory.CreateDirectory(materialsPath);
        }
        
        // Generate filename from material name
        var safeName = string.Join("_", _currentMaterial.Name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Material";
        }
        
        var filePath = Path.Combine(materialsPath, safeName + MaterialSerializer.FileExtension);
        
        // Avoid overwriting - add number suffix if needed
        int suffix = 1;
        while (File.Exists(filePath))
        {
            filePath = Path.Combine(materialsPath, $"{safeName}_{suffix}{MaterialSerializer.FileExtension}");
            suffix++;
        }
        
        SaveMaterial(filePath);
    }
    
    private string GetMaterialsPath()
    {
        if (!_projectManager.HasProject)
            return string.Empty;
            
        var assetsPath = _projectManager.GetAssetPath();
        if (string.IsNullOrEmpty(assetsPath))
            return string.Empty;
            
        return Path.Combine(assetsPath, "Materials");
    }
}
