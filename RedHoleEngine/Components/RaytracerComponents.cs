using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

/// <summary>
/// Component that marks an entity's mesh for raytracing.
/// </summary>
public struct RaytracerMeshComponent : IComponent
{
    public bool Enabled;
    public bool StaticOnly;

    public RaytracerMeshComponent(bool enabled = true)
    {
        Enabled = enabled;
        StaticOnly = true;
    }
}
