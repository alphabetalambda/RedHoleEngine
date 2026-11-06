using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Engine;
using RedHoleEngine.Particles;
using RedHoleEngine.Physics;
using RedHoleEngine.Platform;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Debug;
using RedHoleEngine.Rendering.PBR;
using RedHoleEngine.Rendering.Rasterization;
using RedHoleEngine.Rendering.Raytracing;
using RedHoleEngine.Rendering.UI;
using RedHoleEngine.Rendering.Upscaling;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Tests.Rendering;

public class RaytracerSettingsCoverageTests
{
    [Fact]
    public void Presets_ExposeDocumentedSampleCounts()
    {
        var fast = RaytracerPresetUtilities.GetPresetValues(RaytracerQualityPreset.Fast);
        Assert.Equal(1, fast.RaysPerPixel);
        Assert.Equal(1, fast.MaxBounces);
        Assert.False(fast.Accumulate);
        Assert.False(fast.Denoise);

        var balanced = RaytracerPresetUtilities.GetPresetValues(RaytracerQualityPreset.Balanced);
        Assert.Equal(2, balanced.RaysPerPixel);
        Assert.Equal(2, balanced.MaxBounces);
        Assert.True(balanced.Accumulate);
        Assert.False(balanced.Denoise);

        var quality = RaytracerPresetUtilities.GetPresetValues(RaytracerQualityPreset.Quality);
        Assert.Equal(4, quality.RaysPerPixel);
        Assert.Equal(4, quality.MaxBounces);
        Assert.Equal(2, quality.SamplesPerFrame);
        Assert.True(quality.Denoise);

        var custom = RaytracerPresetUtilities.GetPresetValues(RaytracerQualityPreset.Custom);
        Assert.Equal(balanced.RaysPerPixel, custom.RaysPerPixel);
        Assert.Equal(balanced.MaxBounces, custom.MaxBounces);
    }

    [Fact]
    public void ApplyPreset_CustomLeavesManualValues()
    {
        var settings = new RaytracerSettings { RaysPerPixel = 7, MaxBounces = 3 };
        settings.ApplyPreset(RaytracerQualityPreset.Custom);
        Assert.Equal(RaytracerQualityPreset.Custom, settings.Preset);
        Assert.Equal(7, settings.RaysPerPixel);
        Assert.Equal(3, settings.MaxBounces);

        settings.ApplyPreset(RaytracerQualityPreset.Quality);
        Assert.Equal(4, settings.RaysPerPixel);
        Assert.Equal(4, settings.MaxBounces);
        Assert.True(settings.Denoise);
        Assert.Equal(RaytracerQualityPreset.Quality, settings.Preset);
    }

    [Fact]
    public void ApplyLensingQuality_WritesBaseSteps()
    {
        var settings = new RaytracerSettings { LensingMaxSteps = 20, LensingStepSize = 0.9f };
        settings.ApplyLensingQuality(LensingQuality.Custom);
        Assert.Equal(LensingQuality.Custom, settings.LensingQuality);
        Assert.Equal(20, settings.LensingMaxSteps);
        Assert.Equal(0.9f, settings.LensingStepSize);

        settings.ApplyLensingQuality(LensingQuality.Low);
        Assert.Equal(48, settings.LensingMaxSteps);
        Assert.Equal(0.5f, settings.LensingStepSize);
        Assert.Equal(6, settings.LensingBvhCheckInterval);

        settings.ApplyLensingQuality(LensingQuality.Medium);
        Assert.Equal(96, settings.LensingMaxSteps);
        Assert.Equal(0.35f, settings.LensingStepSize);
        Assert.Equal(4, settings.LensingBvhCheckInterval);

        settings.ApplyLensingQuality(LensingQuality.High);
        Assert.Equal(160, settings.LensingMaxSteps);
        Assert.Equal(0.2f, settings.LensingStepSize);
        Assert.Equal(3, settings.LensingBvhCheckInterval);

        settings.ApplyLensingQuality(LensingQuality.Ultra);
        Assert.Equal(256, settings.LensingMaxSteps);
        Assert.Equal(0.12f, settings.LensingStepSize);
        Assert.Equal(2, settings.LensingBvhCheckInterval);
    }

    [Fact]
    public void Clamp_PullsSamplesAndPostIntoRange()
    {
        var settings = new RaytracerSettings
        {
            MaxRaysPerPixelLimit = 0,
            MaxBouncesLimit = -4,
            MaxSamplesPerFrameLimit = 0,
            RaysPerPixel = 100,
            MaxBounces = 100,
            SamplesPerFrame = 100,
            LensingMaxSteps = 4,
            LensingStepSize = 4f,
            LensingBvhCheckInterval = 0,
            LensingMaxDistance = 10f,
            ATrousIterations = 9,
            ATrousColorSigma = 0f,
            ATrousNormalSigma = 5f,
            ATrousDepthSigma = -1f,
            SVGFTemporalAlpha = 4f,
            SVGFSpatialIterations = 0,
            SVGFPhiColor = 0.2f,
            SVGFPhiNormal = 1000f,
            BloomThreshold = 0.1f,
            BloomIntensity = 2f,
            Exposure = 0f,
            VignetteIntensity = 3f,
            FilmGrainIntensity = 1f,
            VolumetricIntensity = 9f,
            GodRayIntensity = -1f,
            UpscalerSharpness = 4f
        };

        settings.Clamp();

        Assert.Equal(1, settings.MaxRaysPerPixelLimit);
        Assert.Equal(1, settings.RaysPerPixel);
        Assert.Equal(1, settings.MaxBounces);
        Assert.Equal(1, settings.SamplesPerFrame);
        Assert.Equal(16, settings.LensingMaxSteps);
        Assert.Equal(1f, settings.LensingStepSize);
        Assert.Equal(1, settings.LensingBvhCheckInterval);
        Assert.Equal(50f, settings.LensingMaxDistance);
        Assert.Equal(6, settings.ATrousIterations);
        Assert.Equal(0.01f, settings.ATrousColorSigma);
        Assert.Equal(1f, settings.ATrousNormalSigma);
        Assert.Equal(0.01f, settings.ATrousDepthSigma);
        Assert.Equal(1f, settings.SVGFTemporalAlpha);
        Assert.Equal(1, settings.SVGFSpatialIterations);
        Assert.Equal(1f, settings.SVGFPhiColor);
        Assert.Equal(256f, settings.SVGFPhiNormal);
        Assert.Equal(0.5f, settings.BloomThreshold);
        Assert.Equal(1f, settings.BloomIntensity);
        Assert.Equal(0.1f, settings.Exposure);
        Assert.Equal(1f, settings.VignetteIntensity);
        Assert.Equal(0.1f, settings.FilmGrainIntensity);
        Assert.Equal(2f, settings.VolumetricIntensity);
        Assert.Equal(0f, settings.GodRayIntensity);
        Assert.Equal(1f, settings.UpscalerSharpness);
    }

    [Theory]
    [InlineData(UpscaleMethod.None, UpscaleQuality.Performance, 1f)]
    [InlineData(UpscaleMethod.FSR2, UpscaleQuality.Native, 1f)]
    [InlineData(UpscaleMethod.FSR2, UpscaleQuality.UltraQuality, 0.77f)]
    [InlineData(UpscaleMethod.FSR2, UpscaleQuality.Quality, 0.67f)]
    [InlineData(UpscaleMethod.FSR2, UpscaleQuality.Balanced, 0.58f)]
    [InlineData(UpscaleMethod.FSR2, UpscaleQuality.Performance, 0.50f)]
    [InlineData(UpscaleMethod.DLSS, UpscaleQuality.UltraPerformance, 0.33f)]
    public void GetUpscaleRenderScale_FollowsQuality(UpscaleMethod method, UpscaleQuality quality, float scale)
    {
        var settings = new RaytracerSettings { UpscaleMethod = method, UpscaleQuality = quality };
        Assert.Equal(scale, settings.GetUpscaleRenderScale(), precision: 2);
    }

    [Fact]
    public void ApplyPerformanceProfile_CopiesAndClamps()
    {
        var settings = new RaytracerSettings();
        settings.ApplyPerformanceProfile(PerformanceProfileType.Ultra);
        var ultra = PerformanceProfiles.Get(PerformanceProfileType.Ultra);
        Assert.Equal(Math.Clamp(ultra.RaysPerPixel, 1, settings.MaxRaysPerPixelLimit), settings.RaysPerPixel);
        Assert.Equal(ultra.Denoise, settings.Denoise);
        Assert.Equal(ultra.LensingQuality, settings.LensingQuality);

        settings.ApplyPerformanceProfile(PerformanceProfiles.Get(PerformanceProfileType.LowEnd));
        Assert.True(settings.RaysPerPixel >= 1);
        settings.ApplyAutoProfile();
        Assert.True(settings.RaysPerPixel >= 1);
        Assert.InRange(settings.Exposure, 0.1f, 5f);
    }
}

public class RaytracerMeshBuilderCoverageTests
{
    [Fact]
    public void Build_EmptyInput_ReturnsEmptyMesh()
    {
        var data = RaytracerMeshBuilder.Build(Array.Empty<RaytracerTriangle>());
        Assert.Empty(data.Nodes);
        Assert.Empty(data.Triangles);
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<RaytracerTriangle>() >= 144);
    }

    [Fact]
    public void Build_SingleTriangle_IsOneLeafContainingAllVertices()
    {
        var triangle = Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 7);
        var data = RaytracerMeshBuilder.Build(new[] { triangle }, maxLeafSize: 4);

        Assert.Single(data.Nodes);
        Assert.Single(data.Triangles);
        Assert.Equal(1, data.Nodes[0].TriCount);
        Assert.Equal(0, data.Nodes[0].LeftFirst);
        Assert.Equal(7, data.Triangles[0].MaterialIndex);
        Assert.Equal(Vector3.Zero, data.Nodes[0].BoundsMin);
        Assert.Equal(new Vector3(1f, 1f, 0f), data.Nodes[0].BoundsMax);
    }

    [Fact]
    public void Build_SplitsUntilLeavesHoldTheRequestedCount()
    {
        var triangles = new List<RaytracerTriangle>();
        for (int i = 0; i < 8; i++)
        {
            var origin = new Vector3(i * 3f, 0f, (i % 2) * 5f);
            triangles.Add(Triangle(origin, origin + Vector3.UnitX, origin + Vector3.UnitY, i));
        }

        var data = RaytracerMeshBuilder.Build(triangles, maxLeafSize: 2);
        Assert.Equal(8, data.Triangles.Length);
        Assert.Contains(data.Nodes, node => node.TriCount == 0);
        Assert.All(data.Nodes.Where(node => node.TriCount > 0), node => Assert.InRange(node.TriCount, 1, 2));

        var materials = data.Triangles.Select(tri => tri.MaterialIndex).OrderBy(id => id).ToArray();
        Assert.Equal(Enumerable.Range(0, 8).ToArray(), materials);

        var root = data.Nodes[0];
        Assert.True(root.BoundsMin.X <= 0.01f);
        Assert.True(root.BoundsMax.X >= 7 * 3f);
        foreach (var tri in data.Triangles)
        {
            AssertContained(root, tri.V0);
            AssertContained(root, tri.V1);
            AssertContained(root, tri.V2);
        }
    }

    private static void AssertContained(RaytracerBvhNode node, Vector3 point)
    {
        Assert.True(point.X >= node.BoundsMin.X - 0.001f && point.X <= node.BoundsMax.X + 0.001f);
        Assert.True(point.Y >= node.BoundsMin.Y - 0.001f && point.Y <= node.BoundsMax.Y + 0.001f);
        Assert.True(point.Z >= node.BoundsMin.Z - 0.001f && point.Z <= node.BoundsMax.Z + 0.001f);
    }

    private static RaytracerTriangle Triangle(Vector3 a, Vector3 b, Vector3 c, int material)
    {
        return new RaytracerTriangle
        {
            V0 = a,
            V1 = b,
            V2 = c,
            MaterialIndex = material,
            Normal = Vector3.UnitZ,
            UV0 = Vector2.Zero,
            UV1 = Vector2.UnitX,
            UV2 = Vector2.UnitY,
            Albedo = Vector4.One
        };
    }
}

public class RaytracerMeshSystemCoverageTests
{
    [Fact]
    public void Update_SkipsWithoutComputeOrWhenRasterized()
    {
        var world = new World();
        var system = world.AddSystem(new RaytracerMeshSystem());
        Assert.Equal(-50, system.Priority);

        var backend = new RecordingGraphicsBackend { SupportsComputeShaders = false };
        system.SetBackend(backend);
        system.Update(0.016f);
        Assert.Empty(backend.RaytracerUploads);

        backend.SupportsComputeShaders = true;
        backend.RenderSettings.Mode = RenderMode.Rasterized;
        system.Update(0.016f);
        Assert.Empty(backend.RaytracerUploads);
    }

    [Fact]
    public void Update_UploadsTransformedTriangles_AndSkipsDynamicOrDisabled()
    {
        var world = new World();
        var resources = new ResourceManager();
        resources.Add("cube", Mesh.CreateCube(2f));
        var backend = new RecordingGraphicsBackend();
        var system = world.AddSystem(new RaytracerMeshSystem());
        system.SetBackend(backend);

        var visible = world.CreateEntity();
        world.AddComponent(visible, new TransformComponent(new Vector3(5f, 0f, 0f)));
        world.AddComponent(visible, new MeshComponent(resources.GetHandle<Mesh>("cube")));
        world.AddComponent(visible, new RaytracerMeshComponent(true) { StaticOnly = true });
        world.AddComponent(visible, new MaterialComponent
        {
            PbrMaterialId = 4,
            BaseColor = new Vector4(0.2f, 0.3f, 0.4f, 1f),
            EmissiveColor = new Vector3(0.5f, 0f, 0f)
        });

        var disabled = world.CreateEntity();
        world.AddComponent(disabled, new TransformComponent(Vector3.Zero));
        world.AddComponent(disabled, new MeshComponent(resources.GetHandle<Mesh>("cube")));
        world.AddComponent(disabled, new RaytracerMeshComponent(false));

        var dynamicBody = world.CreateEntity();
        world.AddComponent(dynamicBody, new TransformComponent(Vector3.Zero));
        world.AddComponent(dynamicBody, new MeshComponent(resources.GetHandle<Mesh>("cube")));
        world.AddComponent(dynamicBody, new RaytracerMeshComponent(true) { StaticOnly = true });
        world.AddComponent(dynamicBody, new RigidBodyComponent { Type = RigidBodyType.Dynamic });

        system.Update(0.016f);
        system.Update(0.016f);

        Assert.Single(backend.RaytracerUploads);
        var triangles = backend.RaytracerUploads[0].Triangles;
        Assert.Equal(12, triangles.Length);
        Assert.All(triangles, tri => Assert.Equal(4, tri.MaterialIndex));
        Assert.Contains(triangles, tri => tri.V0.X > 4f || tri.V1.X > 4f || tri.V2.X > 4f);
        Assert.Equal(new Vector4(0.2f, 0.3f, 0.4f, 1f), triangles[0].Albedo);
        Assert.Equal(new Vector4(0.5f, 0f, 0f, 1f), triangles[0].Emissive);
        Assert.True(triangles[0].Normal.Length() > 0.9f);
    }

    [Fact]
    public void Update_AppendsLaserBillboards_WhenACameraIsSet()
    {
        var world = new World();
        var backend = new RecordingGraphicsBackend();
        var system = world.AddSystem(new RaytracerMeshSystem());
        system.SetBackend(backend);
        system.SetCamera(new Camera(new Vector3(0f, 0f, 8f)));
        world.AddComponent(world.CreateEntity(), new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = Vector3.UnitX * 2f,
            Width = 0.2f,
            BeamColor = new Vector4(1f, 0f, 0f, 1f),
            CoreColor = Vector4.One,
            CoreWidth = 0.5f,
            Intensity = 2f
        });

        system.Update(0.016f);
        var triangles = backend.RaytracerUploads[0].Triangles;
        Assert.Equal(4, triangles.Length);
        Assert.Equal(50f, triangles[0].Emissive.X, precision: 3);
        Assert.Equal(100f, triangles[2].Emissive.X, precision: 3);
        Assert.All(triangles, tri => Assert.True(tri.Normal.Length() > 0.9f));
    }
}

internal sealed class RecordingGraphicsBackend : IGraphicsBackend
{
    public RaytracerSettings RaytracerSettings { get; } = new();
    public RenderSettings RenderSettings { get; } = new();
    public bool SupportsComputeShaders { get; set; } = true;
    public bool SupportsParticleRendering => false;
    public bool SupportsDebugRendering => false;
    public GraphicsBackendType BackendType => GraphicsBackendType.Vulkan;
    public List<RaytracerMeshData> RaytracerUploads { get; } = new();
    public List<RasterMeshData> RasterUploads { get; } = new();
    public int MaterialUploads { get; private set; }

    public void SetRaytracerMeshData(RaytracerMeshData data) => RaytracerUploads.Add(data);
    public void SetRasterMeshData(RasterMeshData data) => RasterUploads.Add(data);
    public void UploadMaterials(MaterialLibrary library) => MaterialUploads++;
    public void Initialize() { }
    public void Render(Camera camera, BlackHole blackHole, float time) { }
    public void Render(Camera camera, BlackHole blackHole, float time, DebugDrawManager? debugDraw) { }
    public void Render(Camera camera, BlackHole blackHole, float time, DebugDrawManager? debugDraw, ParticlePool? particles) { }
    public void Resize(int width, int height) { }
    public void SetUiDrawData(UiDrawData drawData) { }
    public void SetEnvironmentMap(EnvironmentMap? envMap) { }
    public void Dispose() { }
}
