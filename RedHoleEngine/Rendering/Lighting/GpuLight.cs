using System.Numerics;
using System.Runtime.InteropServices;
using RedHoleEngine.Components;

namespace RedHoleEngine.Rendering.Lighting;

/// <summary>
/// GPU light record. Layout matches the std430 <c>GpuLight</c> struct in
/// raytracer_vulkan.comp (64 bytes, binding 11).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = SizeInBytes)]
public struct GpuLight
{
    /// <summary>
    /// Size of one light record in bytes
    /// </summary>
    public const int SizeInBytes = 64;

    /// <summary>
    /// Maximum number of lights uploaded to the raytracer (MAX_LIGHTS in the shader)
    /// </summary>
    public const int MaxLights = 64;

    [FieldOffset(0)] public Vector3 Position;      // World position (point/spot)
    [FieldOffset(12)] public float Range;          // <= 0 means unbounded
    [FieldOffset(16)] public Vector3 Direction;    // Normalized direction the light travels
    [FieldOffset(28)] public int Type;             // LightType
    [FieldOffset(32)] public Vector3 Color;        // Linear RGB
    [FieldOffset(44)] public float Intensity;
    [FieldOffset(48)] public float SpotInnerCos;   // cos(inner half-angle)
    [FieldOffset(52)] public float SpotOuterCos;   // cos(outer half-angle)
    [FieldOffset(56)] public int CastShadows;      // 1 = trace shadow rays
    [FieldOffset(60)] public float Pad0;

    /// <summary>
    /// Build a GPU record from a light component and its world-space placement.
    /// </summary>
    public static GpuLight FromComponent(in LightComponent light, Vector3 position, Vector3 forward)
    {
        float lengthSq = forward.LengthSquared();
        var direction = lengthSq > 1e-12f && float.IsFinite(lengthSq)
            ? forward / MathF.Sqrt(lengthSq)
            : -Vector3.UnitZ;

        // Keep outer >= inner and both inside (0, 90) degrees so the shader's
        // smoothstep(outerCos, innerCos, ...) always has outerCos <= innerCos.
        float outer = Math.Clamp(light.OuterConeAngle, 0.01f, 89.9f);
        float inner = Math.Clamp(light.InnerConeAngle, 0f, outer);
        const float degToRad = MathF.PI / 180f;

        return new GpuLight
        {
            Position = position,
            Range = MathF.Max(light.Range, 0f),
            Direction = direction,
            Type = (int)light.Type,
            Color = Vector3.Max(light.Color, Vector3.Zero),
            Intensity = MathF.Max(light.Intensity, 0f),
            SpotInnerCos = MathF.Cos(inner * degToRad),
            SpotOuterCos = MathF.Cos(outer * degToRad),
            CastShadows = light.CastShadows ? 1 : 0
        };
    }
}
