using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedHoleEngine.Rendering.PBR;

/// <summary>
/// Serializable material data for .rhmat files
/// </summary>
public class MaterialFileData
{
    /// <summary>
    /// File format version for compatibility
    /// </summary>
    public int Version { get; set; } = 1;
    
    /// <summary>
    /// Material name
    /// </summary>
    public string Name { get; set; } = "New Material";
    
    // Base Color
    public float[] BaseColor { get; set; } = { 1f, 1f, 1f, 1f };
    public string? BaseColorTexture { get; set; }
    
    // Metallic-Roughness
    public float Metallic { get; set; } = 0f;
    public float Roughness { get; set; } = 0.5f;
    public string? MetallicRoughnessTexture { get; set; }
    
    // Normal
    public string? NormalTexture { get; set; }
    public float NormalScale { get; set; } = 1f;
    
    // Occlusion
    public string? OcclusionTexture { get; set; }
    public float OcclusionStrength { get; set; } = 1f;
    
    // Emissive
    public float[] EmissiveColor { get; set; } = { 0f, 0f, 0f };
    public float EmissiveIntensity { get; set; } = 1f;
    public string? EmissiveTexture { get; set; }
    
    // Additional properties
    public float IndexOfRefraction { get; set; } = 1.5f;
    public float SpecularFactor { get; set; } = 1f;
    public string AlphaMode { get; set; } = "Opaque";
    public float AlphaCutoff { get; set; } = 0.5f;
    public bool DoubleSided { get; set; } = false;
    
    // Extensions
    public float ClearcoatFactor { get; set; } = 0f;
    public float ClearcoatRoughness { get; set; } = 0f;
    public float SubsurfaceFactor { get; set; } = 0f;
    public float[] SubsurfaceColor { get; set; } = { 1f, 0.2f, 0.1f };
    public float AnisotropyStrength { get; set; } = 0f;
    public float AnisotropyRotation { get; set; } = 0f;
}

/// <summary>
/// Serializes and deserializes PBR materials to/from .rhmat files
/// </summary>
public static class MaterialSerializer
{
    /// <summary>
    /// File extension for RedHole material files
    /// </summary>
    public const string FileExtension = ".rhmat";
    
    /// <summary>
    /// File filter for file dialogs
    /// </summary>
    public const string FileFilter = "Material Files (*.rhmat)|*.rhmat|All Files (*.*)|*.*";
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    /// <summary>
    /// Save a PBR material to a .rhmat file
    /// </summary>
    public static void SaveToFile(PbrMaterial material, string filePath)
    {
        var data = ToFileData(material);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(filePath, json);
    }
    
    /// <summary>
    /// Load a PBR material from a .rhmat file
    /// </summary>
    public static PbrMaterial LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var data = JsonSerializer.Deserialize<MaterialFileData>(json, JsonOptions);
        
        if (data == null)
            throw new InvalidDataException($"Failed to parse material file: {filePath}");
            
        return FromFileData(data);
    }
    
    /// <summary>
    /// Try to load a material from file
    /// </summary>
    public static bool TryLoadFromFile(string filePath, out PbrMaterial? material, out string? error)
    {
        material = null;
        error = null;
        
        try
        {
            material = LoadFromFile(filePath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
    
    /// <summary>
    /// Serialize a material to JSON string
    /// </summary>
    public static string ToJson(PbrMaterial material)
    {
        var data = ToFileData(material);
        return JsonSerializer.Serialize(data, JsonOptions);
    }
    
    /// <summary>
    /// Deserialize a material from JSON string
    /// </summary>
    public static PbrMaterial FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<MaterialFileData>(json, JsonOptions);
        
        if (data == null)
            throw new InvalidDataException("Failed to parse material JSON");
            
        return FromFileData(data);
    }
    
    /// <summary>
    /// Convert PbrMaterial to serializable file data
    /// </summary>
    public static MaterialFileData ToFileData(PbrMaterial material)
    {
        return new MaterialFileData
        {
            Version = 1,
            Name = material.Name,
            
            BaseColor = new[] 
            { 
                material.BaseColorFactor.X, 
                material.BaseColorFactor.Y, 
                material.BaseColorFactor.Z, 
                material.BaseColorFactor.W 
            },
            BaseColorTexture = material.BaseColorTexturePath,
            
            Metallic = material.MetallicFactor,
            Roughness = material.RoughnessFactor,
            MetallicRoughnessTexture = material.MetallicRoughnessTexturePath,
            
            NormalTexture = material.NormalTexturePath,
            NormalScale = material.NormalScale,
            
            OcclusionTexture = material.OcclusionTexturePath,
            OcclusionStrength = material.OcclusionStrength,
            
            EmissiveColor = new[] 
            { 
                material.EmissiveFactor.X, 
                material.EmissiveFactor.Y, 
                material.EmissiveFactor.Z 
            },
            EmissiveIntensity = material.EmissiveIntensity,
            EmissiveTexture = material.EmissiveTexturePath,
            
            IndexOfRefraction = material.IndexOfRefraction,
            SpecularFactor = material.SpecularFactor,
            AlphaMode = material.AlphaMode.ToString(),
            AlphaCutoff = material.AlphaCutoff,
            DoubleSided = material.DoubleSided,
            
            ClearcoatFactor = material.ClearcoatFactor,
            ClearcoatRoughness = material.ClearcoatRoughness,
            SubsurfaceFactor = material.SubsurfaceFactor,
            SubsurfaceColor = new[] 
            { 
                material.SubsurfaceColor.X, 
                material.SubsurfaceColor.Y, 
                material.SubsurfaceColor.Z 
            },
            AnisotropyStrength = material.AnisotropyStrength,
            AnisotropyRotation = material.AnisotropyRotation
        };
    }
    
    /// <summary>
    /// Convert file data back to PbrMaterial
    /// </summary>
    public static PbrMaterial FromFileData(MaterialFileData data)
    {
        var material = new PbrMaterial
        {
            Name = data.Name,
            
            BaseColorFactor = new Vector4(
                data.BaseColor.Length > 0 ? data.BaseColor[0] : 1f,
                data.BaseColor.Length > 1 ? data.BaseColor[1] : 1f,
                data.BaseColor.Length > 2 ? data.BaseColor[2] : 1f,
                data.BaseColor.Length > 3 ? data.BaseColor[3] : 1f
            ),
            BaseColorTexturePath = data.BaseColorTexture,
            
            MetallicFactor = data.Metallic,
            RoughnessFactor = data.Roughness,
            MetallicRoughnessTexturePath = data.MetallicRoughnessTexture,
            
            NormalTexturePath = data.NormalTexture,
            NormalScale = data.NormalScale,
            
            OcclusionTexturePath = data.OcclusionTexture,
            OcclusionStrength = data.OcclusionStrength,
            
            EmissiveFactor = new Vector3(
                data.EmissiveColor.Length > 0 ? data.EmissiveColor[0] : 0f,
                data.EmissiveColor.Length > 1 ? data.EmissiveColor[1] : 0f,
                data.EmissiveColor.Length > 2 ? data.EmissiveColor[2] : 0f
            ),
            EmissiveIntensity = data.EmissiveIntensity,
            EmissiveTexturePath = data.EmissiveTexture,
            
            IndexOfRefraction = data.IndexOfRefraction,
            SpecularFactor = data.SpecularFactor,
            AlphaMode = Enum.TryParse<AlphaMode>(data.AlphaMode, out var mode) ? mode : AlphaMode.Opaque,
            AlphaCutoff = data.AlphaCutoff,
            DoubleSided = data.DoubleSided,
            
            ClearcoatFactor = data.ClearcoatFactor,
            ClearcoatRoughness = data.ClearcoatRoughness,
            SubsurfaceFactor = data.SubsurfaceFactor,
            SubsurfaceColor = new Vector3(
                data.SubsurfaceColor.Length > 0 ? data.SubsurfaceColor[0] : 1f,
                data.SubsurfaceColor.Length > 1 ? data.SubsurfaceColor[1] : 0.2f,
                data.SubsurfaceColor.Length > 2 ? data.SubsurfaceColor[2] : 0.1f
            ),
            AnisotropyStrength = data.AnisotropyStrength,
            AnisotropyRotation = data.AnisotropyRotation
        };
        
        return material;
    }
    
    /// <summary>
    /// Get a list of all .rhmat files in a directory
    /// </summary>
    public static string[] GetMaterialFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<string>();
            
        return Directory.GetFiles(directory, $"*{FileExtension}", SearchOption.AllDirectories);
    }
    
    /// <summary>
    /// Load all materials from a directory
    /// </summary>
    public static List<PbrMaterial> LoadAllFromDirectory(string directory, out List<string> errors)
    {
        var materials = new List<PbrMaterial>();
        errors = new List<string>();
        
        foreach (var file in GetMaterialFiles(directory))
        {
            if (TryLoadFromFile(file, out var material, out var error))
            {
                materials.Add(material!);
            }
            else
            {
                errors.Add($"{Path.GetFileName(file)}: {error}");
            }
        }
        
        return materials;
    }
}
