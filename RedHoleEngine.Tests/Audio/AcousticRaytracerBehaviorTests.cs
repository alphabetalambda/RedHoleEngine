using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Audio;

public class AcousticRaytracerBehaviorTests
{
    [Fact]
    public void QualityPresets_IncreaseRayCounts()
    {
        Assert.True(AcousticQualitySettings.Low.RaysPerSource < AcousticQualitySettings.Medium.RaysPerSource);
        Assert.True(AcousticQualitySettings.Medium.RaysPerSource < AcousticQualitySettings.High.RaysPerSource);
        Assert.True(AcousticQualitySettings.High.RaysPerSource < AcousticQualitySettings.Cinematic.RaysPerSource);
        Assert.False(AcousticQualitySettings.Low.EnableTransmission);
        Assert.True(AcousticQualitySettings.High.EnableDiffraction);
        Assert.Equal(343f, AcousticQualitySettings.Medium.SpeedOfSound);
    }

    [Fact]
    public void DirectPath_UsesInverseSquareDelay_AndDopplerFromMotion()
    {
        using var world = new World();
        var settings = AcousticQualitySettings.Low;
        settings.RaysPerSource = 0;
        settings.EnableRelativisticEffects = false;
        settings.MaxDistance = 100f;
        var tracer = new AcousticRaytracer(world, settings);
        tracer.UpdateSceneCache();

        var still = tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(10, 0, 0), Vector3.Zero);
        var direct = Assert.Single(still);
        Assert.True(direct.IsDirect);
        Assert.Equal(PathType.Direct, direct.Type);
        Assert.Equal(10f, direct.TotalDistance, 1e-3f);
        Assert.Equal(10f / settings.SpeedOfSound, direct.TotalTime, 1e-4f);
        Assert.Equal(1f, direct.DopplerShift, 1e-4f);
        float attenuation = 1f / (1f + 100f * 0.01f);
        Assert.Equal(attenuation, direct.FinalResponse.Average, 1e-4f);

        var approaching = tracer.TracePaths(Vector3.Zero, new Vector3(20, 0, 0), new Vector3(10, 0, 0), Vector3.Zero);
        Assert.True(approaching[0].DopplerShift > 1f);

        var recedingListener = tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(10, 0, 0), new Vector3(20, 0, 0));
        Assert.True(recedingListener[0].DopplerShift < 1f);

        // Source speed equal to the speed of sound used to divide by zero (MathF.Sign(0) == 0).
        var sonic = tracer.TracePaths(Vector3.Zero, new Vector3(settings.SpeedOfSound, 0, 0), new Vector3(10, 0, 0), Vector3.Zero);
        Assert.True(float.IsFinite(sonic[0].DopplerShift));
        Assert.True(sonic[0].DopplerShift > 10f);

        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero));
        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(500, 0, 0), Vector3.Zero));
    }

    [Fact]
    public void Occluder_BlocksWhenTransmissionIsOff_AndDimsWhenOn()
    {
        using var world = new World();
        var wall = world.CreateEntity();
        world.AddComponent(wall, new TransformComponent(new Vector3(10, 0, 0)));
        world.AddComponent(wall, AcousticSurfaceComponent.Create(AcousticMaterial.EventHorizon));

        var blocked = new AcousticQualitySettings
        {
            RaysPerSource = 0,
            MaxDistance = 100f,
            EnableTransmission = false,
            EnableRelativisticEffects = false,
            SpeedOfSound = 343f
        };
        var tracer = new AcousticRaytracer(world, blocked);
        tracer.UpdateSceneCache();
        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero));

        blocked.EnableTransmission = true;
        var open = new AcousticRaytracer(world, blocked);
        open.UpdateSceneCache();
        // Transmission stays enabled, but a zero-transmission material still reports a fully
        // occluded response. The path is kept and tagged as transmission rather than dropped.
        var blockedPath = Assert.Single(open.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero));
        Assert.Equal(PathType.Transmission, blockedPath.Type);
        Assert.Equal(0f, blockedPath.FinalResponse.Average, 1e-4f);

        world.GetComponent<AcousticSurfaceComponent>(wall).Material = AcousticMaterial.Glass;
        var glass = new AcousticRaytracer(world, blocked);
        glass.UpdateSceneCache();
        var paths = glass.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero);
        var transmitted = Assert.Single(paths);
        Assert.Equal(PathType.Transmission, transmitted.Type);
        Assert.False(transmitted.IsDirect);
        Assert.True(transmitted.FinalResponse.Average < 1f);
        Assert.True(transmitted.FinalResponse.Average > 0f);
    }

    [Fact]
    public void Gravity_RedshiftsSoundNearTheHorizon()
    {
        using var world = new World();
        var hole = world.CreateEntity();
        world.AddComponent(hole, new TransformComponent(Vector3.Zero));
        world.AddComponent(hole, GravitySourceComponent.CreateBlackHole(1f)); // rs = 2

        var settings = AcousticQualitySettings.Low;
        settings.RaysPerSource = 0;
        settings.EnableRelativisticEffects = true;
        settings.MaxDistance = 50f;
        var tracer = new AcousticRaytracer(world, settings);
        tracer.UpdateSceneCache();

        var near = tracer.TracePaths(new Vector3(2.1f, 0, 0), Vector3.Zero, new Vector3(8, 0, 0), Vector3.Zero);
        Assert.True(near[0].GravitationalShift < 1f);
        Assert.True(near[0].GravitationalShift > 0f);

        var inside = tracer.TracePaths(new Vector3(2.02f, 0, 0), Vector3.Zero, new Vector3(12, 0, 0), Vector3.Zero);
        Assert.True(inside[0].GravitationalShift < near[0].GravitationalShift);
    }
}
