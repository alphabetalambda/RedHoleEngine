using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

public struct UnpixComponent : IComponent
{
    public float CubeSize;
    public float DissolveDuration;
    public float StartDelay;
    public float VelocityScale;
    public bool SpawnOnStart;
    public bool HideSource;
    public int MaxCubes;
    
    // Physics options
    public bool UsePhysics;
    public float CubeMass;
    public float CubeRestitution;
    public float CubeFriction;
    public float InitialImpulseScale;

    internal bool Spawned;
    internal float Elapsed;

    public UnpixComponent(float cubeSize = 0.5f, float dissolveDuration = 2f)
    {
        CubeSize = cubeSize;
        DissolveDuration = dissolveDuration;
        StartDelay = 0f;
        VelocityScale = 0.6f;
        SpawnOnStart = true;
        HideSource = true;
        MaxCubes = 4000;
        
        // Physics defaults
        UsePhysics = false;
        CubeMass = 0.5f;
        CubeRestitution = 0.3f;
        CubeFriction = 0.5f;
        InitialImpulseScale = 2.0f;
        
        Spawned = false;
        Elapsed = 0f;
    }
}

public struct UnpixPieceComponent : IComponent
{
    public float Age;
    public float Lifetime;
    public Vector3 StartScale;
    public bool UsePhysics;
    
    // Only used when UsePhysics is false (legacy kinematic mode)
    public Vector3 Velocity;
}
