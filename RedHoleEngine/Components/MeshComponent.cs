using RedHoleEngine.Core.ECS;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Components;

/// <summary>
/// Component that attaches a mesh to an entity for rendering
/// </summary>
public struct MeshComponent : IComponent
{
    /// <summary>
    /// Handle to the mesh resource
    /// </summary>
    public ResourceHandle<Mesh> MeshHandle;
    
    /// <summary>
    /// Whether to cast shadows
    /// </summary>
    public bool CastShadows;
    
    /// <summary>
    /// Whether to receive shadows
    /// </summary>
    public bool ReceiveShadows;
    
    /// <summary>
    /// Render layer mask
    /// </summary>
    public uint LayerMask;
    
    /// <summary>
    /// Whether this mesh is visible
    /// </summary>
    public bool Visible;

    public MeshComponent()
    {
        MeshHandle = default;
        CastShadows = true;
        ReceiveShadows = true;
        LayerMask = 1;
        Visible = true;
    }

    public MeshComponent(ResourceHandle<Mesh> meshHandle)
    {
        MeshHandle = meshHandle;
        CastShadows = true;
        ReceiveShadows = true;
        LayerMask = 1;
        Visible = true;
    }
}

/// <summary>
/// Component for rendering settings/material properties.
/// Can reference a PBR material from the MaterialLibrary via PbrMaterialId,
/// or use inline material properties (BaseColor, Metallic, Roughness, EmissiveColor).
/// </summary>
public struct MaterialComponent : IComponent
{
    /// <summary>
    /// ID of a PBR material in the MaterialLibrary (-1 = use inline properties)
    /// </summary>
    public int PbrMaterialId;
    
    /// <summary>
    /// Base color (albedo) - used when PbrMaterialId is -1
    /// </summary>
    public System.Numerics.Vector4 BaseColor;
    
    /// <summary>
    /// Metallic factor (0-1) - used when PbrMaterialId is -1
    /// </summary>
    public float Metallic;
    
    /// <summary>
    /// Roughness factor (0-1) - used when PbrMaterialId is -1
    /// </summary>
    public float Roughness;
    
    /// <summary>
    /// Emissive color and intensity - used when PbrMaterialId is -1
    /// </summary>
    public System.Numerics.Vector3 EmissiveColor;
    
    /// <summary>
    /// Whether to use raytracing for this material
    /// </summary>
    public bool UseRaytracing;

    /// <summary>
    /// Whether this component uses a PBR material from the library
    /// </summary>
    public readonly bool UsesPbrMaterial => PbrMaterialId >= 0;

    public static MaterialComponent Default => new()
    {
        PbrMaterialId = -1,
        BaseColor = System.Numerics.Vector4.One,
        Metallic = 0f,
        Roughness = 0.5f,
        EmissiveColor = System.Numerics.Vector3.Zero,
        UseRaytracing = false
    };

    /// <summary>
    /// Create a material component that references a PBR material from the library
    /// </summary>
    public static MaterialComponent FromPbrMaterial(int materialId) => new()
    {
        PbrMaterialId = materialId,
        BaseColor = System.Numerics.Vector4.One,
        Metallic = 0f,
        Roughness = 0.5f,
        EmissiveColor = System.Numerics.Vector3.Zero,
        UseRaytracing = false
    };

    public static MaterialComponent CreateEmissive(System.Numerics.Vector3 color, float intensity = 1f) => new()
    {
        PbrMaterialId = -1,
        BaseColor = System.Numerics.Vector4.One,
        Metallic = 0f,
        Roughness = 0.5f,
        EmissiveColor = color * intensity,
        UseRaytracing = false
    };
}
