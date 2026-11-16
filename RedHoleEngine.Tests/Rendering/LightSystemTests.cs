using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Backends;
using RedHoleEngine.Rendering.Lighting;
using RedHoleEngine.Serialization;

namespace RedHoleEngine.Tests.Rendering;

public class GpuLightLayoutTests
{
    [Fact]
    public void GpuLight_MatchesStd430Layout()
    {
        Assert.Equal(64, Marshal.SizeOf<GpuLight>());
        Assert.Equal(64, GpuLight.SizeInBytes);
        Assert.Equal(64, System.Runtime.CompilerServices.Unsafe.SizeOf<GpuLight>());

        Assert.Equal(0, Offset<GpuLight>(nameof(GpuLight.Position)));
        Assert.Equal(12, Offset<GpuLight>(nameof(GpuLight.Range)));
        Assert.Equal(16, Offset<GpuLight>(nameof(GpuLight.Direction)));
        Assert.Equal(28, Offset<GpuLight>(nameof(GpuLight.Type)));
        Assert.Equal(32, Offset<GpuLight>(nameof(GpuLight.Color)));
        Assert.Equal(44, Offset<GpuLight>(nameof(GpuLight.Intensity)));
        Assert.Equal(48, Offset<GpuLight>(nameof(GpuLight.SpotInnerCos)));
        Assert.Equal(52, Offset<GpuLight>(nameof(GpuLight.SpotOuterCos)));
        Assert.Equal(56, Offset<GpuLight>(nameof(GpuLight.CastShadows)));
        Assert.Equal(60, Offset<GpuLight>(nameof(GpuLight.Pad0)));
    }

    [Fact]
    public void RaytracerUniforms_LightCountReusesTrailingPadSlot()
    {
        Assert.Equal(224, Marshal.SizeOf<RaytracerUniforms>());
        Assert.Equal(216, Offset<RaytracerUniforms>(nameof(RaytracerUniforms.LightCount)));
        Assert.Equal(220, Offset<RaytracerUniforms>(nameof(RaytracerUniforms.Pad10)));
    }

    [Fact]
    public void LightTypeValues_MatchShaderConstants()
    {
        string source = File.ReadAllText(ShaderPath("raytracer_vulkan.comp"));

        Assert.Equal((int)LightType.Directional, ShaderIntConstant(source, "LIGHT_TYPE_DIRECTIONAL"));
        Assert.Equal((int)LightType.Point, ShaderIntConstant(source, "LIGHT_TYPE_POINT"));
        Assert.Equal((int)LightType.Spot, ShaderIntConstant(source, "LIGHT_TYPE_SPOT"));
        Assert.Equal(GpuLight.MaxLights, ShaderIntConstant(source, "MAX_LIGHTS"));
    }

    /// <summary>
    /// Reflects the committed SPIR-V so a stale or mismatched binary fails here instead of on the GPU.
    /// </summary>
    [Fact]
    public void CompiledShader_LightStructAndBindingsMatchCpuLayout()
    {
        var module = SpirvReflection.Parse(File.ReadAllBytes(ShaderPath("raytracer_vulkan.spv")));

        var expected = new (string Member, string Field)[]
        {
            ("position", nameof(GpuLight.Position)),
            ("range", nameof(GpuLight.Range)),
            ("direction", nameof(GpuLight.Direction)),
            ("type", nameof(GpuLight.Type)),
            ("color", nameof(GpuLight.Color)),
            ("intensity", nameof(GpuLight.Intensity)),
            ("spotInnerCos", nameof(GpuLight.SpotInnerCos)),
            ("spotOuterCos", nameof(GpuLight.SpotOuterCos)),
            ("castShadows", nameof(GpuLight.CastShadows)),
            ("pad0", nameof(GpuLight.Pad0))
        };

        var members = module.MemberOffsets("GpuLight");
        Assert.Equal(expected.Length, members.Count);
        foreach (var (member, field) in expected)
        {
            Assert.Equal(Offset<GpuLight>(field), members[member]);
        }

        // Runtime array of lights is tightly packed at the C# struct size
        Assert.Contains((uint)GpuLight.SizeInBytes, module.ArrayStrides);

        var uniforms = module.MemberOffsets("UniformBlock");
        Assert.Equal(Offset<RaytracerUniforms>(nameof(RaytracerUniforms.LightCount)), uniforms["u_LightCount"]);
        Assert.Equal(Offset<RaytracerUniforms>(nameof(RaytracerUniforms.PhotonSphereRadius)), uniforms["u_PhotonSphereRadius"]);

        // The light buffer must not collide with the denoiser G-buffer images (9, 10)
        Assert.Equal(9u, module.Bindings["normalImage"]);
        Assert.Equal(10u, module.Bindings["albedoImage"]);
        Assert.Equal(12, module.Bindings.Count);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => (uint)i), module.Bindings.Values.OrderBy(b => b));
    }

    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    private static string ShaderPath(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", fileName);
        Assert.True(File.Exists(path), $"Shader file not copied to test output: {path}");
        return path;
    }

    private static int ShaderIntConstant(string source, string name)
    {
        var match = Regex.Match(source, $@"const\s+int\s+{name}\s*=\s*(\d+)\s*;");
        Assert.True(match.Success, $"Shader constant {name} not found");
        return int.Parse(match.Groups[1].Value);
    }
}

public class LightCollectorTests
{
    [Fact]
    public void Collect_EmptyWorld_ReturnsZero()
    {
        using var world = new World();
        var lights = new GpuLight[GpuLight.MaxLights];

        Assert.Equal(0, LightCollector.Collect(world, lights));
    }

    [Fact]
    public void Collect_DirectionalLight_UsesTransformForward()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f); // forward (-Z) -> -Y
        world.AddComponent(entity, new TransformComponent(new Vector3(1f, 2f, 3f), rotation));
        world.AddComponent(entity, LightComponent.CreateDirectional(new Vector3(1f, 0.9f, 0.8f), 3f, castShadows: false));

        var lights = new GpuLight[4];
        Assert.Equal(1, LightCollector.Collect(world, lights));

        var light = lights[0];
        Assert.Equal((int)LightType.Directional, light.Type);
        AssertVector(new Vector3(0f, -1f, 0f), light.Direction);
        Assert.Equal(new Vector3(1f, 0.9f, 0.8f), light.Color);
        Assert.Equal(3f, light.Intensity);
        Assert.Equal(0, light.CastShadows);
        Assert.Equal(0f, light.Range);
    }

    [Fact]
    public void Collect_PointAndSpotLights_CopyPlacementAndConeCosines()
    {
        using var world = new World();

        var point = world.CreateEntity();
        world.AddComponent(point, new TransformComponent(new Vector3(4f, 5f, 6f)));
        world.AddComponent(point, LightComponent.CreatePoint(new Vector3(0f, 1f, 0f), 50f, range: 12f));

        var spot = world.CreateEntity();
        world.AddComponent(spot, new TransformComponent(new Vector3(-1f, 8f, 2f)));
        world.AddComponent(spot, LightComponent.CreateSpot(Vector3.One, 20f, range: 30f, innerConeAngle: 15f, outerConeAngle: 40f));

        var lights = new GpuLight[4];
        Assert.Equal(2, LightCollector.Collect(world, lights));

        var gpuPoint = lights.First(l => l.Type == (int)LightType.Point);
        Assert.Equal(new Vector3(4f, 5f, 6f), gpuPoint.Position);
        Assert.Equal(12f, gpuPoint.Range);
        Assert.Equal(50f, gpuPoint.Intensity);
        Assert.Equal(1, gpuPoint.CastShadows);

        var gpuSpot = lights.First(l => l.Type == (int)LightType.Spot);
        Assert.Equal(new Vector3(-1f, 8f, 2f), gpuSpot.Position);
        AssertVector(-Vector3.UnitZ, gpuSpot.Direction);
        Assert.Equal(MathF.Cos(15f * MathF.PI / 180f), gpuSpot.SpotInnerCos, 5);
        Assert.Equal(MathF.Cos(40f * MathF.PI / 180f), gpuSpot.SpotOuterCos, 5);
        Assert.True(gpuSpot.SpotInnerCos >= gpuSpot.SpotOuterCos);
    }

    [Fact]
    public void Collect_SkipsDisabledAndNonContributingLights()
    {
        using var world = new World();
        AddLight(world, new LightComponent { Enabled = false });
        AddLight(world, new LightComponent { Intensity = 0f });
        AddLight(world, new LightComponent { Color = Vector3.Zero });
        AddLight(world, default(LightComponent)); // default struct is disabled
        AddLight(world, new LightComponent { Intensity = 7f });

        var lights = new GpuLight[8];
        Assert.Equal(1, LightCollector.Collect(world, lights));
        Assert.Equal(7f, lights[0].Intensity);
    }

    [Fact]
    public void Collect_LightWithoutTransform_SitsAtOriginFacingForward()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, LightComponent.CreateSpot(Vector3.One));

        var lights = new GpuLight[1];
        Assert.Equal(1, LightCollector.Collect(world, lights));
        Assert.Equal(Vector3.Zero, lights[0].Position);
        AssertVector(-Vector3.UnitZ, lights[0].Direction);
    }

    [Fact]
    public void Collect_ClampsToMaxLights_AndKeepsDirectionalLightsFirst()
    {
        using var world = new World();
        for (int i = 0; i < GpuLight.MaxLights + 10; i++)
        {
            AddLight(world, LightComponent.CreatePoint(Vector3.One, 1f + i));
        }
        // Added last, but must survive clamping
        AddLight(world, LightComponent.CreateDirectional(Vector3.One, 2f));

        var lights = new GpuLight[GpuLight.MaxLights * 2];
        int count = LightCollector.Collect(world, lights);

        Assert.Equal(GpuLight.MaxLights, count);
        Assert.Equal((int)LightType.Directional, lights[0].Type);
        Assert.All(lights.Skip(1).Take(count - 1), l => Assert.Equal((int)LightType.Point, l.Type));
        // Nothing is written past the clamp
        Assert.All(lights.Skip(count), l => Assert.Equal(0f, l.Intensity));
    }

    [Fact]
    public void Collect_RespectsSmallDestination()
    {
        using var world = new World();
        for (int i = 0; i < 5; i++)
        {
            AddLight(world, new LightComponent());
        }

        Assert.Equal(3, LightCollector.Collect(world, new GpuLight[3]));
        Assert.Equal(0, LightCollector.Collect(world, Span<GpuLight>.Empty));
    }

    [Fact]
    public void FromComponent_SanitizesInvalidValues()
    {
        var component = new LightComponent
        {
            Type = LightType.Spot,
            Color = new Vector3(-1f, 0.5f, 2f),
            Intensity = 4f,
            Range = -5f,
            InnerConeAngle = 80f,
            OuterConeAngle = 30f
        };

        var light = GpuLight.FromComponent(in component, Vector3.Zero, Vector3.Zero);

        Assert.Equal(new Vector3(0f, 0.5f, 2f), light.Color);
        Assert.Equal(0f, light.Range);
        AssertVector(-Vector3.UnitZ, light.Direction); // degenerate forward falls back
        Assert.True(light.SpotInnerCos >= light.SpotOuterCos); // inner clamped to outer
        Assert.Equal(0f, light.Pad0);

        var scaled = GpuLight.FromComponent(in component, Vector3.Zero, new Vector3(0f, 10f, 0f));
        AssertVector(Vector3.UnitY, scaled.Direction);
    }

    [Fact]
    public void LightSystem_UploadsCollectedLightsEveryFrame()
    {
        using var world = new World();
        var system = world.AddSystem(new LightSystem());
        var backend = new RecordingGraphicsBackend { SupportsComputeShaders = false };
        system.SetBackend(backend);

        AddLight(world, LightComponent.CreatePoint(Vector3.One, 5f));

        system.Update(0.016f);
        Assert.Empty(backend.LightUploads);

        backend.SupportsComputeShaders = true;
        system.Update(0.016f);
        Assert.Single(backend.LightUploads);
        Assert.Single(backend.LightUploads[0]);
        Assert.Equal(5f, backend.LightUploads[0][0].Intensity);
        Assert.Equal(1, system.LightCount);

        // Removing every light must still notify the backend so it returns to the fallback rig
        foreach (var entity in world.Query<LightComponent>().ToList())
        {
            world.RemoveComponent<LightComponent>(entity);
        }

        system.Update(0.016f);
        Assert.Equal(2, backend.LightUploads.Count);
        Assert.Empty(backend.LightUploads[1]);
        Assert.Equal(0, system.LightCount);
    }

    [Fact]
    public void Serialization_RoundTrip_PreservesLightComponent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_light_{Guid.NewGuid()}.rhes");
        try
        {
            using var world = new World();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new TransformComponent(new Vector3(1f, 2f, 3f)));
            world.AddComponent(entity, new LightComponent
            {
                Type = LightType.Spot,
                Color = new Vector3(0.25f, 0.5f, 0.75f),
                Intensity = 42.5f,
                Range = 17f,
                InnerConeAngle = 12f,
                OuterConeAngle = 34f,
                CastShadows = false,
                Enabled = true
            });

            var disabled = world.CreateEntity();
            world.AddComponent(disabled, new LightComponent { Type = LightType.Directional, Enabled = false });

            var serializer = new SceneSerializer();
            serializer.SaveToFile(world, path);

            using var loadWorld = new World();
            serializer.LoadFromFile(loadWorld, path);

            var loadedLights = loadWorld.Query<LightComponent>()
                .Select(e => loadWorld.GetComponent<LightComponent>(e))
                .ToList();
            Assert.Equal(2, loadedLights.Count);

            var spot = loadedLights.Single(l => l.Type == LightType.Spot);
            Assert.Equal(new Vector3(0.25f, 0.5f, 0.75f), spot.Color);
            Assert.Equal(42.5f, spot.Intensity);
            Assert.Equal(17f, spot.Range);
            Assert.Equal(12f, spot.InnerConeAngle);
            Assert.Equal(34f, spot.OuterConeAngle);
            Assert.False(spot.CastShadows);
            Assert.True(spot.Enabled);

            var directional = loadedLights.Single(l => l.Type == LightType.Directional);
            Assert.False(directional.Enabled);
            Assert.True(directional.CastShadows);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void AddLight(World world, LightComponent light)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent());
        world.AddComponent(entity, light);
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}");
    }
}

/// <summary>
/// Minimal SPIR-V reader: just enough to check struct member offsets, array strides and bindings.
/// </summary>
internal sealed class SpirvReflection
{
    private const uint Magic = 0x07230203;
    private const int OpName = 5;
    private const int OpMemberName = 6;
    private const int OpDecorate = 71;
    private const int OpMemberDecorate = 72;
    private const uint DecorationArrayStride = 6;
    private const uint DecorationBinding = 33;
    private const uint DecorationOffset = 35;

    private readonly Dictionary<uint, string> _names = new();
    private readonly Dictionary<(uint Type, uint Member), string> _memberNames = new();
    private readonly Dictionary<(uint Type, uint Member), uint> _memberOffsets = new();
    private readonly Dictionary<uint, uint> _bindingsById = new();

    public List<uint> ArrayStrides { get; } = new();

    /// <summary>
    /// Binding number per descriptor, keyed by variable name (or "#id" for unnamed blocks)
    /// </summary>
    public Dictionary<string, uint> Bindings { get; } = new();

    public static SpirvReflection Parse(byte[] bytes)
    {
        Assert.True(bytes.Length % 4 == 0 && bytes.Length > 20, "SPIR-V size must be a multiple of 4");
        var words = MemoryMarshal.Cast<byte, uint>(bytes).ToArray();
        Assert.Equal(Magic, words[0]);

        var module = new SpirvReflection();
        int i = 5;
        while (i < words.Length)
        {
            int opcode = (int)(words[i] & 0xFFFF);
            int length = (int)(words[i] >> 16);
            Assert.True(length > 0 && i + length <= words.Length, "Malformed SPIR-V instruction");

            switch (opcode)
            {
                case OpName:
                    module._names[words[i + 1]] = ReadString(words, i + 2, i + length);
                    break;
                case OpMemberName:
                    module._memberNames[(words[i + 1], words[i + 2])] = ReadString(words, i + 3, i + length);
                    break;
                case OpMemberDecorate when words[i + 3] == DecorationOffset:
                    module._memberOffsets[(words[i + 1], words[i + 2])] = words[i + 4];
                    break;
                case OpDecorate when words[i + 2] == DecorationBinding:
                    module._bindingsById[words[i + 1]] = words[i + 3];
                    break;
                case OpDecorate when words[i + 2] == DecorationArrayStride:
                    module.ArrayStrides.Add(words[i + 3]);
                    break;
            }

            i += length;
        }

        foreach (var (id, binding) in module._bindingsById)
        {
            string name = module._names.TryGetValue(id, out var n) && n.Length > 0 ? n : $"#{id}";
            module.Bindings[name] = binding;
        }

        return module;
    }

    public Dictionary<string, int> MemberOffsets(string structName)
    {
        // A struct that is also copied into a local gets a second, undecorated type with the
        // same name; only the buffer-backed one carries Offset decorations.
        var ids = _names
            .Where(kv => kv.Value == structName && _memberOffsets.Keys.Any(k => k.Type == kv.Key))
            .Select(kv => kv.Key)
            .ToList();
        Assert.True(ids.Count == 1, $"Expected exactly one laid-out SPIR-V type named {structName}, found {ids.Count}");

        return _memberOffsets
            .Where(kv => kv.Key.Type == ids[0])
            .ToDictionary(kv => _memberNames[kv.Key], kv => (int)kv.Value);
    }

    private static string ReadString(uint[] words, int start, int end)
    {
        var bytes = MemoryMarshal.AsBytes(words.AsSpan(start, end - start));
        int terminator = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(terminator >= 0 ? bytes.Slice(0, terminator) : bytes);
    }
}
