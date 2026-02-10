using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Editor.Project;
using RedHoleEngine.Rendering.PBR;
using Silk.NET.OpenGL;

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
    
    // Texture paths
    private string _baseColorTexturePath = string.Empty;
    private string _metallicRoughnessTexturePath = string.Empty;
    private string _normalTexturePath = string.Empty;
    private string _emissiveTexturePath = string.Empty;
    
    // Preview rendering
    private GL? _gl;
    private MaterialPreviewRenderer? _previewRenderer;
    private uint _previewTexture;
    private const int PreviewSize = 192;
    private bool _previewNeedsUpdate = true;
    private float _previewRotationX;
    private float _previewRotationY;
    private bool _isDraggingPreview;
    private Vector2 _lastMousePos;
    
    private static readonly string[] TextureFilter = { "png", "jpg", "jpeg", "tga", "bmp" };
    
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
    /// Initialize the preview renderer with OpenGL context.
    /// Must be called after GL is available.
    /// </summary>
    public void InitializePreview(GL gl)
    {
        _gl = gl;
        _previewRenderer = new MaterialPreviewRenderer(PreviewSize, PreviewSize);
        
        // Create preview texture
        _previewTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _previewTexture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        
        // Initialize with empty data
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 
                PreviewSize, PreviewSize, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)0);
        }
        
        _previewNeedsUpdate = true;
    }
    
    /// <summary>
    /// Clean up preview resources
    /// </summary>
    public void DisposePreview()
    {
        if (_previewTexture != 0 && _gl != null)
        {
            _gl.DeleteTexture(_previewTexture);
            _previewTexture = 0;
        }
    }
    
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
        _previewNeedsUpdate = true;
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
            _previewNeedsUpdate = true;
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
        MarkDirty();
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
        
        // Texture paths
        _baseColorTexturePath = _currentMaterial.BaseColorTexturePath ?? string.Empty;
        _metallicRoughnessTexturePath = _currentMaterial.MetallicRoughnessTexturePath ?? string.Empty;
        _normalTexturePath = _currentMaterial.NormalTexturePath ?? string.Empty;
        _emissiveTexturePath = _currentMaterial.EmissiveTexturePath ?? string.Empty;
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
        
        // Texture paths
        _currentMaterial.BaseColorTexturePath = string.IsNullOrEmpty(_baseColorTexturePath) ? null : _baseColorTexturePath;
        _currentMaterial.MetallicRoughnessTexturePath = string.IsNullOrEmpty(_metallicRoughnessTexturePath) ? null : _metallicRoughnessTexturePath;
        _currentMaterial.NormalTexturePath = string.IsNullOrEmpty(_normalTexturePath) ? null : _normalTexturePath;
        _currentMaterial.EmissiveTexturePath = string.IsNullOrEmpty(_emissiveTexturePath) ? null : _emissiveTexturePath;
    }
    
    private void MarkDirty()
    {
        MarkDirty();
        _previewNeedsUpdate = true;
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
            MarkDirty();
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
                MarkDirty();
            }
            
            // Texture slot
            DrawTextureSlot("Albedo Texture", ref _baseColorTexturePath, "BaseColor");
        }
        
        // Metallic-Roughness
        if (ImGui.CollapsingHeader("Metallic-Roughness", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.SliderFloat("Metallic", ref _metallic, 0f, 1f))
            {
                MarkDirty();
            }
            
            if (ImGui.SliderFloat("Roughness", ref _roughness, 0f, 1f))
            {
                MarkDirty();
            }
            
            // Quick buttons for common values
            ImGui.Text("Quick:");
            ImGui.SameLine();
            if (ImGui.SmallButton("Dielectric"))
            {
                _metallic = 0f;
                _roughness = 0.5f;
                MarkDirty();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Metal"))
            {
                _metallic = 1f;
                _roughness = 0.3f;
                MarkDirty();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Mirror"))
            {
                _metallic = 1f;
                _roughness = 0f;
                MarkDirty();
            }
            
            ImGui.Separator();
            DrawTextureSlot("Metallic/Roughness Map", ref _metallicRoughnessTexturePath, "MetallicRoughness");
            ImGui.TextDisabled("Green=Roughness, Blue=Metallic");
        }
        
        // Normal Map
        if (ImGui.CollapsingHeader("Normal Map"))
        {
            DrawTextureSlot("Normal Map", ref _normalTexturePath, "Normal");
            ImGui.TextDisabled("Tangent-space normal map");
        }
        
        // Emission
        if (ImGui.CollapsingHeader("Emission"))
        {
            if (ImGui.ColorEdit3("Color##Emissive", ref _emissiveColor))
            {
                MarkDirty();
            }
            
            if (ImGui.SliderFloat("Intensity##Emissive", ref _emissiveIntensity, 0f, 20f))
            {
                MarkDirty();
            }
            
            // Quick buttons
            ImGui.Text("Quick:");
            ImGui.SameLine();
            if (ImGui.SmallButton("Off"))
            {
                _emissiveColor = Vector3.Zero;
                _emissiveIntensity = 1f;
                MarkDirty();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Glow"))
            {
                _emissiveIntensity = 5f;
                MarkDirty();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Bright"))
            {
                _emissiveIntensity = 15f;
                MarkDirty();
            }
            
            ImGui.Separator();
            DrawTextureSlot("Emissive Map", ref _emissiveTexturePath, "Emissive");
        }
        
        // Advanced
        if (ImGui.CollapsingHeader("Advanced"))
        {
            if (ImGui.SliderFloat("IOR", ref _ior, 1f, 3f, "%.2f"))
            {
                MarkDirty();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Index of Refraction\n1.0 = Air\n1.33 = Water\n1.5 = Glass\n2.4 = Diamond");
            }
            
            if (ImGui.Combo("Alpha Mode", ref _alphaMode, AlphaModeNames, AlphaModeNames.Length))
            {
                MarkDirty();
            }
            
            if (_alphaMode == 1) // Mask
            {
                if (ImGui.SliderFloat("Alpha Cutoff", ref _alphaCutoff, 0f, 1f))
                {
                    MarkDirty();
                }
            }
            
            if (ImGui.Checkbox("Double Sided", ref _doubleSided))
            {
                MarkDirty();
            }
        }
        
        // Clearcoat
        if (ImGui.CollapsingHeader("Clearcoat"))
        {
            if (ImGui.SliderFloat("Clearcoat", ref _clearcoat, 0f, 1f))
            {
                MarkDirty();
            }
            
            if (_clearcoat > 0f)
            {
                if (ImGui.SliderFloat("Clearcoat Roughness", ref _clearcoatRoughness, 0f, 1f))
                {
                    MarkDirty();
                }
            }
            
            ImGui.TextDisabled("Use for car paint, lacquered wood, etc.");
        }
        
        // Preview
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Preview", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawPreview();
            
            ImGui.Separator();
            
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
    
    private void DrawPreview()
    {
        if (_previewRenderer == null || _gl == null || _previewTexture == 0)
        {
            ImGui.TextDisabled("Preview not available");
            ImGui.TextDisabled("(GL context not initialized)");
            return;
        }
        
        // Update preview if material changed
        if (_previewNeedsUpdate && _currentMaterial != null)
        {
            SyncToMaterial();
            _previewRenderer.SetRotation(_previewRotationX, _previewRotationY);
            _previewRenderer.Render(_currentMaterial);
            
            // Upload to texture
            unsafe
            {
                fixed (byte* ptr = _previewRenderer.Buffer)
                {
                    _gl.BindTexture(TextureTarget.Texture2D, _previewTexture);
                    _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 
                        (uint)PreviewSize, (uint)PreviewSize, 
                        PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
                }
            }
            
            _previewNeedsUpdate = false;
        }
        
        // Center the preview image
        var availWidth = ImGui.GetContentRegionAvail().X;
        var offset = (availWidth - PreviewSize) * 0.5f;
        if (offset > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        
        // Draw the preview image
        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.Image((IntPtr)_previewTexture, new Vector2(PreviewSize, PreviewSize));
        
        // Handle mouse interaction for rotation
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Drag to rotate");
            
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _isDraggingPreview = true;
                _lastMousePos = ImGui.GetMousePos();
            }
        }
        
        if (_isDraggingPreview)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var mousePos = ImGui.GetMousePos();
                var delta = mousePos - _lastMousePos;
                
                _previewRotationY += delta.X * 0.01f;
                _previewRotationX += delta.Y * 0.01f;
                
                // Clamp X rotation to avoid flipping
                _previewRotationX = Math.Clamp(_previewRotationX, -MathF.PI * 0.4f, MathF.PI * 0.4f);
                
                _lastMousePos = mousePos;
                _previewNeedsUpdate = true;
            }
            else
            {
                _isDraggingPreview = false;
            }
        }
        
        // Reset rotation button
        if (offset > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        if (ImGui.SmallButton("Reset View"))
        {
            _previewRotationX = 0;
            _previewRotationY = 0;
            _previewNeedsUpdate = true;
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
    
    private void DrawTextureSlot(string label, ref string texturePath, string uniqueId)
    {
        ImGui.PushID(uniqueId);
        
        bool hasTexture = !string.IsNullOrEmpty(texturePath);
        string displayText = hasTexture ? Path.GetFileName(texturePath) : "(none)";
        
        ImGui.Text(label + ":");
        ImGui.SameLine();
        
        // Show texture path or (none)
        if (hasTexture)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), displayText);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(texturePath);
            }
        }
        else
        {
            ImGui.TextDisabled(displayText);
        }
        
        // Browse button
        ImGui.SameLine();
        if (ImGui.SmallButton("..."))
        {
            // For now, just show the textures folder
            var texturesPath = GetTexturesPath();
            if (!string.IsNullOrEmpty(texturesPath))
            {
                Console.WriteLine($"Textures folder: {texturesPath}");
                // In a real implementation, this would open a file dialog
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Browse for texture file");
        }
        
        // Clear button (only show if texture is set)
        if (hasTexture)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                texturePath = string.Empty;
                MarkDirty();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Clear texture");
            }
        }
        
        // Manual path input
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##path", ref texturePath, 512))
        {
            MarkDirty();
        }
        
        ImGui.PopID();
    }
    
    private string GetTexturesPath()
    {
        if (!_projectManager.HasProject)
            return string.Empty;
            
        var assetsPath = _projectManager.GetAssetPath();
        if (string.IsNullOrEmpty(assetsPath))
            return string.Empty;
            
        return Path.Combine(assetsPath, "Textures");
    }
}
