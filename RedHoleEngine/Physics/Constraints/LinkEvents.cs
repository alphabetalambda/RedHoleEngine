using System.Numerics;

namespace RedHoleEngine.Physics.Constraints;

/// <summary>
/// Event fired when a link breaks
/// </summary>
public struct LinkBreakEvent
{
    /// <summary>
    /// Unique ID of the broken link
    /// </summary>
    public int LinkId;
    
    /// <summary>
    /// Chain ID if link was part of a chain (-1 if standalone)
    /// </summary>
    public int ChainId;
    
    /// <summary>
    /// Mesh ID if link was part of a mesh (-1 if not)
    /// </summary>
    public int MeshId;
    
    /// <summary>
    /// Entity ID of body A
    /// </summary>
    public int EntityA;
    
    /// <summary>
    /// Entity ID of body B (-1 if world anchor)
    /// </summary>
    public int EntityB;
    
    /// <summary>
    /// World position where the break occurred (midpoint of link)
    /// </summary>
    public Vector3 BreakPoint;
    
    /// <summary>
    /// Direction of the link at break (normalized)
    /// </summary>
    public Vector3 BreakDirection;
    
    /// <summary>
    /// Force magnitude that caused the break
    /// </summary>
    public float BreakForce;
    
    /// <summary>
    /// How much the link was stretched when it broke (ratio, e.g., 1.5 = 50% stretch)
    /// </summary>
    public float StretchRatio;
}

/// <summary>
/// Event fired when a plastic link yields (permanently deforms)
/// </summary>
public struct LinkYieldEvent
{
    /// <summary>
    /// Unique ID of the yielding link
    /// </summary>
    public int LinkId;
    
    /// <summary>
    /// Chain ID if link was part of a chain (-1 if standalone)
    /// </summary>
    public int ChainId;
    
    /// <summary>
    /// Mesh ID if link was part of a mesh (-1 if not)
    /// </summary>
    public int MeshId;
    
    /// <summary>
    /// Rest length before yielding
    /// </summary>
    public float OldRestLength;
    
    /// <summary>
    /// Rest length after yielding
    /// </summary>
    public float NewRestLength;
    
    /// <summary>
    /// Current stretch ratio
    /// </summary>
    public float StretchRatio;
    
    /// <summary>
    /// World position of the link midpoint
    /// </summary>
    public Vector3 Position;
}

/// <summary>
/// Event fired when a link is under significant tension (for monitoring)
/// </summary>
public struct LinkStretchEvent
{
    /// <summary>
    /// Unique ID of the stretched link
    /// </summary>
    public int LinkId;
    
    /// <summary>
    /// Current tension force in the link (Newtons)
    /// </summary>
    public float Tension;
    
    /// <summary>
    /// Current stretch ratio (1.0 = at rest length)
    /// </summary>
    public float StretchRatio;
    
    /// <summary>
    /// Percentage of break threshold reached (0-1)
    /// </summary>
    public float StressLevel;
    
    /// <summary>
    /// World position of the link midpoint
    /// </summary>
    public Vector3 Position;
}

/// <summary>
/// Event fired when a chain is broken into two separate chains
/// </summary>
public struct ChainBreakEvent
{
    /// <summary>
    /// Original chain ID
    /// </summary>
    public int OriginalChainId;
    
    /// <summary>
    /// New chain ID for the first segment (or -1 if only one link remained)
    /// </summary>
    public int NewChainIdA;
    
    /// <summary>
    /// New chain ID for the second segment (or -1 if only one link remained)
    /// </summary>
    public int NewChainIdB;
    
    /// <summary>
    /// Index in the original chain where the break occurred
    /// </summary>
    public int BreakIndex;
    
    /// <summary>
    /// World position of the break
    /// </summary>
    public Vector3 BreakPoint;
}

/// <summary>
/// Event fired when a mesh's structural integrity changes significantly
/// </summary>
public struct MeshDamageEvent
{
    /// <summary>
    /// Mesh ID
    /// </summary>
    public int MeshId;
    
    /// <summary>
    /// Number of links that broke in this event
    /// </summary>
    public int LinksLost;
    
    /// <summary>
    /// Current structural integrity (0-1, percentage of unbroken links)
    /// </summary>
    public float Integrity;
    
    /// <summary>
    /// Previous integrity before damage
    /// </summary>
    public float PreviousIntegrity;
    
    /// <summary>
    /// Center of the damage area
    /// </summary>
    public Vector3 DamageCenter;
    
    /// <summary>
    /// Approximate radius of the damage area
    /// </summary>
    public float DamageRadius;
}

// Delegate types for event handlers
public delegate void LinkBreakHandler(LinkBreakEvent evt);
public delegate void LinkYieldHandler(LinkYieldEvent evt);
public delegate void LinkStretchHandler(LinkStretchEvent evt);
public delegate void ChainBreakHandler(ChainBreakEvent evt);
public delegate void MeshDamageHandler(MeshDamageEvent evt);
