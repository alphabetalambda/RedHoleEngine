using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

/// <summary>
/// Kind of light emitted by a <see cref="LightComponent"/>.
/// Values are shared with the raytracer shader (LIGHT_TYPE_* constants).
/// </summary>
public enum LightType
{
    /// <summary>Infinitely distant light; only the entity's forward direction matters.</summary>
    Directional = 0,

    /// <summary>Omnidirectional light at the entity's position.</summary>
    Point = 1,

    /// <summary>Cone light at the entity's position pointing along its forward direction.</summary>
    Spot = 2
}

/// <summary>
/// A light source for the raytracer's direct lighting.
/// Position and direction come from the entity's <see cref="TransformComponent"/>
/// (lights shine along the transform's forward axis, -Z by convention).
/// </summary>
public struct LightComponent : IComponent
{
    /// <summary>
    /// Kind of light
    /// </summary>
    public LightType Type;

    /// <summary>
    /// Linear RGB colour
    /// </summary>
    public Vector3 Color;

    /// <summary>
    /// Brightness multiplier. Directional lights use it as radiance directly;
    /// point and spot lights are divided by squared distance.
    /// </summary>
    public float Intensity;

    /// <summary>
    /// Distance at which point/spot lights fade to zero. 0 or less means unbounded
    /// (pure inverse-square falloff). Ignored for directional lights.
    /// </summary>
    public float Range;

    /// <summary>
    /// Spot light: half-angle in degrees of the fully lit inner cone
    /// </summary>
    public float InnerConeAngle;

    /// <summary>
    /// Spot light: half-angle in degrees where the light reaches zero
    /// </summary>
    public float OuterConeAngle;

    /// <summary>
    /// Whether the raytracer traces shadow rays for this light
    /// </summary>
    public bool CastShadows;

    /// <summary>
    /// Disabled lights are skipped during collection
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// Create a white point light with sensible defaults
    /// </summary>
    public LightComponent()
    {
        Type = LightType.Point;
        Color = Vector3.One;
        Intensity = 1f;
        Range = 10f;
        InnerConeAngle = 20f;
        OuterConeAngle = 30f;
        CastShadows = true;
        Enabled = true;
    }

    /// <summary>
    /// Create a directional (sun-like) light
    /// </summary>
    public static LightComponent CreateDirectional(Vector3 color, float intensity = 1f, bool castShadows = true)
    {
        return new LightComponent
        {
            Type = LightType.Directional,
            Color = color,
            Intensity = intensity,
            Range = 0f,
            CastShadows = castShadows
        };
    }

    /// <summary>
    /// Create a point light
    /// </summary>
    public static LightComponent CreatePoint(Vector3 color, float intensity = 1f, float range = 10f, bool castShadows = true)
    {
        return new LightComponent
        {
            Type = LightType.Point,
            Color = color,
            Intensity = intensity,
            Range = range,
            CastShadows = castShadows
        };
    }

    /// <summary>
    /// Create a spot light. Cone angles are half-angles in degrees.
    /// </summary>
    public static LightComponent CreateSpot(
        Vector3 color,
        float intensity = 1f,
        float range = 10f,
        float innerConeAngle = 20f,
        float outerConeAngle = 30f,
        bool castShadows = true)
    {
        return new LightComponent
        {
            Type = LightType.Spot,
            Color = color,
            Intensity = intensity,
            Range = range,
            InnerConeAngle = innerConeAngle,
            OuterConeAngle = outerConeAngle,
            CastShadows = castShadows
        };
    }
}
