using System.Numerics;
using System.Runtime.InteropServices;

namespace RedHoleEngine.Rendering.PBR;

/// <summary>
/// PBR (Physically Based Rendering) material definition.
/// Uses the metallic-roughness workflow compatible with glTF 2.0.
/// </summary>
public class PbrMaterial
{
    /// <summary>
    /// Unique identifier for this material
    /// </summary>
    public int Id { get; internal set; }
    
    /// <summary>
    /// Human-readable name for this material
    /// </summary>
    public string Name { get; set; } = "New Material";
    
    // ==========================================
    // Base Color (Albedo)
    // ==========================================
    
    /// <summary>
    /// Base color factor (RGBA). Multiplied with base color texture.
    /// For metallic materials, this is the specular color (F0).
    /// For dielectric materials, this is the diffuse albedo.
    /// </summary>
    public Vector4 BaseColorFactor { get; set; } = Vector4.One;
    
    /// <summary>
    /// Path to base color texture (albedo map). Null if not used.
    /// </summary>
    public string? BaseColorTexturePath { get; set; }
    
    // ==========================================
    // Metallic-Roughness
    // ==========================================
    
    /// <summary>
    /// Metallic factor (0-1). 0 = dielectric, 1 = metal.
    /// Multiplied with metallic texture (blue channel).
    /// </summary>
    public float MetallicFactor { get; set; } = 0f;
    
    /// <summary>
    /// Roughness factor (0-1). 0 = smooth/mirror, 1 = rough/diffuse.
    /// Multiplied with roughness texture (green channel).
    /// </summary>
    public float RoughnessFactor { get; set; } = 0.5f;
    
    /// <summary>
    /// Path to metallic-roughness texture. 
    /// Green channel = roughness, Blue channel = metallic.
    /// Null if not used.
    /// </summary>
    public string? MetallicRoughnessTexturePath { get; set; }
    
    // ==========================================
    // Normal Map
    // ==========================================
    
    /// <summary>
    /// Path to normal map texture (tangent-space). Null if not used.
    /// </summary>
    public string? NormalTexturePath { get; set; }
    
    /// <summary>
    /// Normal map strength multiplier (default 1.0)
    /// </summary>
    public float NormalScale { get; set; } = 1f;
    
    // ==========================================
    // Ambient Occlusion
    // ==========================================
    
    /// <summary>
    /// Path to ambient occlusion texture (R channel). Null if not used.
    /// </summary>
    public string? OcclusionTexturePath { get; set; }
    
    /// <summary>
    /// AO strength multiplier (default 1.0)
    /// </summary>
    public float OcclusionStrength { get; set; } = 1f;
    
    // ==========================================
    // Emission
    // ==========================================
    
    /// <summary>
    /// Emissive color factor (RGB). Multiplied with emissive texture.
    /// Values above 1.0 create HDR emission (bloom).
    /// </summary>
    public Vector3 EmissiveFactor { get; set; } = Vector3.Zero;
    
    /// <summary>
    /// Path to emissive texture. Null if not used.
    /// </summary>
    public string? EmissiveTexturePath { get; set; }
    
    /// <summary>
    /// Emissive intensity multiplier for HDR emission
    /// </summary>
    public float EmissiveIntensity { get; set; } = 1f;
    
    // ==========================================
    // Additional PBR Properties
    // ==========================================
    
    /// <summary>
    /// Index of Refraction for dielectric materials (default 1.5 for glass-like).
    /// Used for Fresnel calculations: F0 = ((ior - 1) / (ior + 1))^2
    /// </summary>
    public float IndexOfRefraction { get; set; } = 1.5f;
    
    /// <summary>
    /// Specular intensity override for dielectric materials (0-1).
    /// Modulates the F0 value.
    /// </summary>
    public float SpecularFactor { get; set; } = 1f;
    
    /// <summary>
    /// Alpha mode: Opaque, Mask, or Blend
    /// </summary>
    public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;
    
    /// <summary>
    /// Alpha cutoff threshold for Mask mode (default 0.5)
    /// </summary>
    public float AlphaCutoff { get; set; } = 0.5f;
    
    /// <summary>
    /// Whether this material is double-sided (renders back faces)
    /// </summary>
    public bool DoubleSided { get; set; } = false;
    
    // ==========================================
    // Clearcoat (Extension)
    // ==========================================
    
    /// <summary>
    /// Clearcoat layer intensity (0-1). 0 = no clearcoat.
    /// Used for car paint, lacquered surfaces, etc.
    /// </summary>
    public float ClearcoatFactor { get; set; } = 0f;
    
    /// <summary>
    /// Clearcoat layer roughness (0-1)
    /// </summary>
    public float ClearcoatRoughness { get; set; } = 0f;
    
    // ==========================================
    // Subsurface Scattering (Extension)
    // ==========================================
    
    /// <summary>
    /// Subsurface scattering intensity (0-1). 0 = no SSS.
    /// Used for skin, wax, marble, etc.
    /// </summary>
    public float SubsurfaceFactor { get; set; } = 0f;
    
    /// <summary>
    /// Subsurface scattering color (light color transmitted through material)
    /// </summary>
    public Vector3 SubsurfaceColor { get; set; } = new Vector3(1f, 0.2f, 0.1f);
    
    // ==========================================
    // Anisotropy (Extension)
    // ==========================================
    
    /// <summary>
    /// Anisotropy strength (-1 to 1). 0 = isotropic.
    /// Used for brushed metal, hair, etc.
    /// </summary>
    public float AnisotropyStrength { get; set; } = 0f;
    
    /// <summary>
    /// Anisotropy rotation angle in radians (0 to 2*PI)
    /// </summary>
    public float AnisotropyRotation { get; set; } = 0f;
    
    // ==========================================
    // Factory Methods
    // ==========================================
    
    /// <summary>
    /// Create a default white dielectric material
    /// </summary>
    public static PbrMaterial Default() => new()
    {
        Name = "Default",
        BaseColorFactor = Vector4.One,
        MetallicFactor = 0f,
        RoughnessFactor = 0.5f
    };
    
    /// <summary>
    /// Create a metallic material
    /// </summary>
    public static PbrMaterial Metal(Vector3 color, float roughness = 0.3f) => new()
    {
        Name = "Metal",
        BaseColorFactor = new Vector4(color, 1f),
        MetallicFactor = 1f,
        RoughnessFactor = roughness
    };
    
    /// <summary>
    /// Create a gold material
    /// </summary>
    public static PbrMaterial Gold(float roughness = 0.2f) => new()
    {
        Name = "Gold",
        BaseColorFactor = new Vector4(1f, 0.766f, 0.336f, 1f),
        MetallicFactor = 1f,
        RoughnessFactor = roughness
    };
    
    /// <summary>
    /// Create a copper material
    /// </summary>
    public static PbrMaterial Copper(float roughness = 0.25f) => new()
    {
        Name = "Copper",
        BaseColorFactor = new Vector4(0.955f, 0.638f, 0.538f, 1f),
        MetallicFactor = 1f,
        RoughnessFactor = roughness
    };
    
    /// <summary>
    /// Create an iron/steel material
    /// </summary>
    public static PbrMaterial Iron(float roughness = 0.4f) => new()
    {
        Name = "Iron",
        BaseColorFactor = new Vector4(0.56f, 0.57f, 0.58f, 1f),
        MetallicFactor = 1f,
        RoughnessFactor = roughness
    };
    
    /// <summary>
    /// Create a plastic material
    /// </summary>
    public static PbrMaterial Plastic(Vector3 color, float roughness = 0.4f) => new()
    {
        Name = "Plastic",
        BaseColorFactor = new Vector4(color, 1f),
        MetallicFactor = 0f,
        RoughnessFactor = roughness,
        IndexOfRefraction = 1.46f // Typical for plastics
    };
    
    /// <summary>
    /// Create a glass material
    /// </summary>
    public static PbrMaterial Glass(float roughness = 0.0f, float ior = 1.5f) => new()
    {
        Name = "Glass",
        BaseColorFactor = new Vector4(1f, 1f, 1f, 0.1f),
        MetallicFactor = 0f,
        RoughnessFactor = roughness,
        IndexOfRefraction = ior,
        AlphaMode = AlphaMode.Blend
    };
    
    /// <summary>
    /// Create an emissive material
    /// </summary>
    public static PbrMaterial Emissive(Vector3 color, float intensity = 5f) => new()
    {
        Name = "Emissive",
        BaseColorFactor = Vector4.One,
        MetallicFactor = 0f,
        RoughnessFactor = 1f,
        EmissiveFactor = color,
        EmissiveIntensity = intensity
    };
    
    /// <summary>
    /// Create a car paint material with clearcoat
    /// </summary>
    public static PbrMaterial CarPaint(Vector3 color, float clearcoat = 1f) => new()
    {
        Name = "Car Paint",
        BaseColorFactor = new Vector4(color, 1f),
        MetallicFactor = 0f,
        RoughnessFactor = 0.4f,
        ClearcoatFactor = clearcoat,
        ClearcoatRoughness = 0.1f
    };
}

/// <summary>
/// Alpha blending mode for materials
/// </summary>
public enum AlphaMode
{
    /// <summary>
    /// Fully opaque, alpha channel ignored
    /// </summary>
    Opaque,
    
    /// <summary>
    /// Alpha test with cutoff threshold
    /// </summary>
    Mask,
    
    /// <summary>
    /// Alpha blending (transparent)
    /// </summary>
    Blend
}

/// <summary>
/// GPU-compatible material data structure for shader upload.
/// Matches the layout expected by the compute shader.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct GpuMaterial
{
    [FieldOffset(0)]  public Vector4 BaseColorFactor;      // 16 bytes
    [FieldOffset(16)] public float MetallicFactor;         // 4 bytes
    [FieldOffset(20)] public float RoughnessFactor;        // 4 bytes
    [FieldOffset(24)] public float NormalScale;            // 4 bytes
    [FieldOffset(28)] public float OcclusionStrength;      // 4 bytes
    [FieldOffset(32)] public Vector3 EmissiveFactor;       // 12 bytes
    [FieldOffset(44)] public float EmissiveIntensity;      // 4 bytes
    [FieldOffset(48)] public float IndexOfRefraction;      // 4 bytes
    [FieldOffset(52)] public float SpecularFactor;         // 4 bytes
    [FieldOffset(56)] public float ClearcoatFactor;        // 4 bytes
    [FieldOffset(60)] public float ClearcoatRoughness;     // 4 bytes
    // Total: 64 bytes
    
    public static GpuMaterial FromPbrMaterial(PbrMaterial mat) => new()
    {
        BaseColorFactor = mat.BaseColorFactor,
        MetallicFactor = mat.MetallicFactor,
        RoughnessFactor = mat.RoughnessFactor,
        NormalScale = mat.NormalScale,
        OcclusionStrength = mat.OcclusionStrength,
        EmissiveFactor = mat.EmissiveFactor,
        EmissiveIntensity = mat.EmissiveIntensity,
        IndexOfRefraction = mat.IndexOfRefraction,
        SpecularFactor = mat.SpecularFactor,
        ClearcoatFactor = mat.ClearcoatFactor,
        ClearcoatRoughness = mat.ClearcoatRoughness
    };
    
    public static GpuMaterial Default => new()
    {
        BaseColorFactor = Vector4.One,
        MetallicFactor = 0f,
        RoughnessFactor = 0.5f,
        NormalScale = 1f,
        OcclusionStrength = 1f,
        EmissiveFactor = Vector3.Zero,
        EmissiveIntensity = 1f,
        IndexOfRefraction = 1.5f,
        SpecularFactor = 1f,
        ClearcoatFactor = 0f,
        ClearcoatRoughness = 0f
    };
}
