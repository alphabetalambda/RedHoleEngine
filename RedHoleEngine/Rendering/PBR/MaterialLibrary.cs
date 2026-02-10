using System.Numerics;

namespace RedHoleEngine.Rendering.PBR;

/// <summary>
/// Manages a collection of PBR materials and provides GPU data for rendering.
/// </summary>
public class MaterialLibrary
{
    private readonly List<PbrMaterial> _materials = new();
    private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
    private GpuMaterial[] _gpuMaterials = Array.Empty<GpuMaterial>();
    private bool _isDirty = true;
    
    /// <summary>
    /// Number of materials in the library
    /// </summary>
    public int Count => _materials.Count;
    
    /// <summary>
    /// Get all materials in the library
    /// </summary>
    public IReadOnlyList<PbrMaterial> Materials => _materials;
    
    /// <summary>
    /// Event raised when materials are modified
    /// </summary>
    public event Action? MaterialsChanged;
    
    public MaterialLibrary()
    {
        // Add default material at index 0
        AddMaterial(PbrMaterial.Default());
    }
    
    /// <summary>
    /// Add a material to the library
    /// </summary>
    /// <returns>Material ID (index)</returns>
    public int AddMaterial(PbrMaterial material)
    {
        int id = _materials.Count;
        material.Id = id;
        _materials.Add(material);
        
        if (!string.IsNullOrEmpty(material.Name))
        {
            _nameToId[material.Name] = id;
        }
        
        _isDirty = true;
        MaterialsChanged?.Invoke();
        return id;
    }
    
    /// <summary>
    /// Get a material by ID
    /// </summary>
    public PbrMaterial? GetMaterial(int id)
    {
        if (id < 0 || id >= _materials.Count)
            return null;
        return _materials[id];
    }
    
    /// <summary>
    /// Get a material by name
    /// </summary>
    public PbrMaterial? GetMaterial(string name)
    {
        if (_nameToId.TryGetValue(name, out int id))
            return _materials[id];
        return null;
    }
    
    /// <summary>
    /// Get material ID by name. Returns 0 (default) if not found.
    /// </summary>
    public int GetMaterialId(string name)
    {
        if (_nameToId.TryGetValue(name, out int id))
            return id;
        return 0;
    }
    
    /// <summary>
    /// Update a material's properties
    /// </summary>
    public void UpdateMaterial(int id, Action<PbrMaterial> updateAction)
    {
        if (id < 0 || id >= _materials.Count)
            return;
            
        updateAction(_materials[id]);
        _isDirty = true;
        MaterialsChanged?.Invoke();
    }
    
    /// <summary>
    /// Remove a material by ID. Note: This invalidates all higher IDs!
    /// Consider marking materials as unused instead.
    /// </summary>
    public bool RemoveMaterial(int id)
    {
        if (id <= 0 || id >= _materials.Count) // Can't remove default material
            return false;
            
        var material = _materials[id];
        if (!string.IsNullOrEmpty(material.Name))
        {
            _nameToId.Remove(material.Name);
        }
        
        _materials.RemoveAt(id);
        
        // Update IDs for all materials after the removed one
        for (int i = id; i < _materials.Count; i++)
        {
            _materials[i].Id = i;
            if (!string.IsNullOrEmpty(_materials[i].Name))
            {
                _nameToId[_materials[i].Name] = i;
            }
        }
        
        _isDirty = true;
        MaterialsChanged?.Invoke();
        return true;
    }
    
    /// <summary>
    /// Get GPU-ready material array for shader upload
    /// </summary>
    public GpuMaterial[] GetGpuMaterials()
    {
        if (_isDirty || _gpuMaterials.Length != _materials.Count)
        {
            _gpuMaterials = new GpuMaterial[_materials.Count];
            for (int i = 0; i < _materials.Count; i++)
            {
                _gpuMaterials[i] = GpuMaterial.FromPbrMaterial(_materials[i]);
            }
            _isDirty = false;
        }
        return _gpuMaterials;
    }
    
    /// <summary>
    /// Check if the material data needs to be re-uploaded to GPU
    /// </summary>
    public bool IsDirty => _isDirty;
    
    /// <summary>
    /// Mark as clean after GPU upload
    /// </summary>
    public void MarkClean() => _isDirty = false;
    
    /// <summary>
    /// Force mark as dirty (e.g., when textures change)
    /// </summary>
    public void MarkDirty() => _isDirty = true;
    
    /// <summary>
    /// Clear all materials and reset to default
    /// </summary>
    public void Clear()
    {
        _materials.Clear();
        _nameToId.Clear();
        _gpuMaterials = Array.Empty<GpuMaterial>();
        
        // Re-add default material
        AddMaterial(PbrMaterial.Default());
    }
    
    /// <summary>
    /// Add standard material presets to the library
    /// </summary>
    public void AddStandardPresets()
    {
        // Metals
        AddMaterial(PbrMaterial.Gold());
        AddMaterial(PbrMaterial.Copper());
        AddMaterial(PbrMaterial.Iron());
        
        var chrome = PbrMaterial.Metal(new Vector3(0.95f, 0.95f, 0.95f), 0.1f);
        chrome.Name = "Chrome";
        AddMaterial(chrome);
        
        var aluminum = PbrMaterial.Metal(new Vector3(0.91f, 0.92f, 0.92f), 0.2f);
        aluminum.Name = "Aluminum";
        AddMaterial(aluminum);
        
        // Dielectrics
        var redPlastic = PbrMaterial.Plastic(new Vector3(0.8f, 0.1f, 0.1f));
        redPlastic.Name = "Red Plastic";
        AddMaterial(redPlastic);
        
        var bluePlastic = PbrMaterial.Plastic(new Vector3(0.1f, 0.1f, 0.8f));
        bluePlastic.Name = "Blue Plastic";
        AddMaterial(bluePlastic);
        
        var greenPlastic = PbrMaterial.Plastic(new Vector3(0.1f, 0.8f, 0.1f));
        greenPlastic.Name = "Green Plastic";
        AddMaterial(greenPlastic);
        
        var roughPlastic = PbrMaterial.Plastic(new Vector3(0.9f, 0.9f, 0.9f), 0.6f);
        roughPlastic.Name = "Rough Plastic";
        AddMaterial(roughPlastic);
        
        // Special
        AddMaterial(PbrMaterial.Glass());
        
        var orangeGlow = PbrMaterial.Emissive(new Vector3(1f, 0.5f, 0.1f), 10f);
        orangeGlow.Name = "Orange Glow";
        AddMaterial(orangeGlow);
        
        var blueGlow = PbrMaterial.Emissive(new Vector3(0.2f, 0.5f, 1f), 10f);
        blueGlow.Name = "Blue Glow";
        AddMaterial(blueGlow);
        
        var redCarPaint = PbrMaterial.CarPaint(new Vector3(0.7f, 0.1f, 0.1f));
        redCarPaint.Name = "Red Car Paint";
        AddMaterial(redCarPaint);
        
        // Organic/Natural
        AddMaterial(new PbrMaterial
        {
            Name = "Wood",
            BaseColorFactor = new Vector4(0.55f, 0.35f, 0.2f, 1f),
            MetallicFactor = 0f,
            RoughnessFactor = 0.7f
        });
        
        AddMaterial(new PbrMaterial
        {
            Name = "Concrete",
            BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1f),
            MetallicFactor = 0f,
            RoughnessFactor = 0.9f
        });
        
        AddMaterial(new PbrMaterial
        {
            Name = "Marble",
            BaseColorFactor = new Vector4(0.95f, 0.95f, 0.95f, 1f),
            MetallicFactor = 0f,
            RoughnessFactor = 0.2f,
            SubsurfaceFactor = 0.3f,
            SubsurfaceColor = new Vector3(0.8f, 0.7f, 0.6f)
        });
    }
}
