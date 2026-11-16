using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Rendering.Lighting;

/// <summary>
/// Gathers <see cref="LightComponent"/>s into the GPU light list.
/// </summary>
public static class LightCollector
{
    /// <summary>
    /// Fill <paramref name="destination"/> with the world's enabled lights and return how many were written.
    /// At most <c>min(destination.Length, GpuLight.MaxLights)</c> lights are written; when the scene has more,
    /// directional lights are kept first (they affect everything), then the rest in query order.
    /// Lights that are disabled or contribute nothing (zero intensity or black) are skipped.
    /// Entities without a <see cref="TransformComponent"/> sit at the origin facing -Z.
    /// </summary>
    public static int Collect(World world, Span<GpuLight> destination)
    {
        int capacity = Math.Min(destination.Length, GpuLight.MaxLights);
        if (capacity <= 0)
            return 0;

        int count = 0;

        // Pass 0: directional lights. Pass 1: everything else.
        for (int pass = 0; pass < 2 && count < capacity; pass++)
        {
            foreach (var entity in world.Query<LightComponent>())
            {
                if (count >= capacity)
                    break;

                ref var light = ref world.GetComponent<LightComponent>(entity);
                bool isDirectional = light.Type == LightType.Directional;
                if (isDirectional != (pass == 0))
                    continue;

                if (!IsContributing(in light))
                    continue;

                var position = Vector3.Zero;
                var forward = -Vector3.UnitZ;
                if (world.TryGetComponent<TransformComponent>(entity, out var transform))
                {
                    position = transform.Position;
                    forward = transform.Forward;
                }

                destination[count++] = GpuLight.FromComponent(in light, position, forward);
            }
        }

        return count;
    }

    private static bool IsContributing(in LightComponent light)
    {
        if (!light.Enabled || !(light.Intensity > 0f))
            return false;

        return light.Color.X > 0f || light.Color.Y > 0f || light.Color.Z > 0f;
    }
}

/// <summary>
/// Collects scene lights every frame and hands them to the graphics backend.
/// </summary>
public class LightSystem : GameSystem
{
    private readonly GpuLight[] _lights = new GpuLight[GpuLight.MaxLights];
    private IGraphicsBackend? _backend;

    public override int Priority => -50;

    /// <summary>
    /// Number of lights sent to the backend by the last update
    /// </summary>
    public int LightCount { get; private set; }

    /// <summary>
    /// Lights sent to the backend by the last update
    /// </summary>
    public ReadOnlySpan<GpuLight> Lights => _lights.AsSpan(0, LightCount);

    public void SetBackend(IGraphicsBackend backend)
    {
        _backend = backend;
    }

    public override void Update(float deltaTime)
    {
        if (_backend == null || !_backend.SupportsComputeShaders)
            return;

        LightCount = LightCollector.Collect(World, _lights);
        _backend.SetLights(Lights);
    }
}
