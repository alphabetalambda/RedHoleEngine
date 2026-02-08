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
/// Component for rendering settings/material properties
/// (Placeholder - will be expanded with full material system)
/// </summary>
public struct MaterialComponent : IComponent
{
    /// <summary>
    /// Base color (albedo)
    /// </summary>
    public System.Numerics.Vector4 BaseColor;
    
    /// <summary>
    /// Metallic factor (0-1)
    /// </summary>
    public float Metallic;
    
    /// <summary>
    /// Roughness factor (0-1)
    /// </summary>
    public float Roughness;
    
    /// <summary>
    /// Emissive color and intensity
    /// </summary>
    public System.Numerics.Vector3 EmissiveColor;
    
    /// <summary>
    /// Whether to use raytracing for this material
    /// </summary>
    public bool UseRaytracing;

    public static MaterialComponent Default => new()
    {
        BaseColor = System.Numerics.Vector4.One,
        Metallic = 0f,
        Roughness = 0.5f,
        EmissiveColor = System.Numerics.Vector3.Zero,
        UseRaytracing = false
    };

    public static MaterialComponent CreateEmissive(System.Numerics.Vector3 color, float intensity = 1f) => new()
    {
        BaseColor = System.Numerics.Vector4.One,
        Metallic = 0f,
        Roughness = 0.5f,
        EmissiveColor = color * intensity,
        UseRaytracing = false
    };
}
