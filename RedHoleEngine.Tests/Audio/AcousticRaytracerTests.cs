using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Audio;

public class AcousticRaytracerTests
{
    private const float SoundSpeed = 343f;

    [Fact]
    public void QualityPresets_UseThePublishedBudgets()
    {
        var low = AcousticQualitySettings.Low;
        Assert.Equal(16, low.RaysPerSource);
        Assert.Equal(2, low.MaxBounces);
        Assert.Equal(100f, low.MaxDistance);
        Assert.Equal(0.01f, low.EnergyThreshold);
        Assert.False(low.EnableDiffraction);
        Assert.False(low.EnableTransmission);
        Assert.True(low.EnableRelativisticEffects);
        Assert.Equal(15f, low.UpdateRate);

        var medium = AcousticQualitySettings.Medium;
        Assert.Equal(64, medium.RaysPerSource);
        Assert.Equal(4, medium.MaxBounces);
        Assert.Equal(200f, medium.MaxDistance);
        Assert.Equal(0.001f, medium.EnergyThreshold);
        Assert.False(medium.EnableDiffraction);
        Assert.True(medium.EnableTransmission);
        Assert.Equal(30f, medium.UpdateRate);
        Assert.Equal(SoundSpeed, medium.SpeedOfSound);

        var high = AcousticQualitySettings.High;
        Assert.Equal(256, high.RaysPerSource);
        Assert.Equal(8, high.MaxBounces);
        Assert.Equal(500f, high.MaxDistance);
        Assert.Equal(0.0001f, high.EnergyThreshold);
        Assert.True(high.EnableDiffraction);
        Assert.Equal(60f, high.UpdateRate);

        var cinematic = AcousticQualitySettings.Cinematic;
        Assert.Equal(1024, cinematic.RaysPerSource);
        Assert.Equal(16, cinematic.MaxBounces);
        Assert.Equal(1000f, cinematic.MaxDistance);
        Assert.Equal(0.00001f, cinematic.EnergyThreshold, 1e-8);
        Assert.True(cinematic.EnableDiffraction);
        Assert.True(cinematic.EnableTransmission);
        Assert.Equal(60f, cinematic.UpdateRate);
    }

    [Fact]
    public void DirectPath_ReportsDistanceDelayAttenuation_AndDistanceBoundaries()
    {
        using var world = new World();
        var tracer = Tracer(world, maxDistance: 10f);
        tracer.UpdateSceneCache();

        var paths = tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(10, 0, 0), Vector3.Zero, rayCount: 0);
        var direct = Assert.Single(paths);
        Assert.True(direct.IsDirect);
        Assert.Equal(PathType.Direct, direct.Type);
        Assert.Equal(10f, direct.TotalDistance, 1e-4);
        Assert.Equal(10f / SoundSpeed, direct.TotalTime, 1e-5);
        Assert.Equal(1f, direct.DopplerShift, 1e-5);
        Assert.Equal(1f, direct.GravitationalShift, 1e-5);
        Assert.Equal(1f, direct.TotalPitchShift, 1e-5);
        float attenuation = 1f / (1f + 100f * 0.01f);
        Assert.Equal(attenuation, direct.FinalResponse.Average, 1e-5);
        Assert.Equal(direct.FinalResponse.Band63Hz, direct.FinalResponse.Band8kHz, 1e-5);
        Assert.Empty(direct.Hits);

        var onLimit = tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(10, 0, 0), Vector3.Zero, 0);
        Assert.Single(onLimit);

        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(10.01f, 0, 0), Vector3.Zero, 0));
        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0));

        var touching = tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(0.001f, 0, 0), Vector3.Zero, 0);
        Assert.Equal(0.001f, Assert.Single(touching).TotalDistance, 1e-6);
    }

    [Fact]
    public void Doppler_RaisesPitchWhenMotionClosesTheGap_AndLowersItWhenTheGapOpens()
    {
        using var world = new World();
        var tracer = Tracer(world, relativistic: false);
        tracer.UpdateSceneCache();

        Vector3 source = Vector3.Zero;
        Vector3 listener = new(10, 0, 0);

        float toward = Doppler(tracer, source, new Vector3(10, 0, 0), listener, Vector3.Zero);
        float away = Doppler(tracer, source, new Vector3(-10, 0, 0), listener, Vector3.Zero);
        float listenerToward = Doppler(tracer, source, Vector3.Zero, listener, new Vector3(-10, 0, 0));
        float listenerAway = Doppler(tracer, source, Vector3.Zero, listener, new Vector3(10, 0, 0));
        float both = Doppler(tracer, source, new Vector3(10, 0, 0), listener, new Vector3(-10, 0, 0));

        Assert.Equal(SoundSpeed / (SoundSpeed - 10f), toward, 1e-4);
        Assert.Equal(SoundSpeed / (SoundSpeed + 10f), away, 1e-4);
        Assert.Equal((SoundSpeed + 10f) / SoundSpeed, listenerToward, 1e-4);
        Assert.Equal((SoundSpeed - 10f) / SoundSpeed, listenerAway, 1e-4);
        Assert.Equal((SoundSpeed + 10f) / (SoundSpeed - 10f), both, 1e-4);
        Assert.True(toward > 1f);
        Assert.True(away < 1f);
        Assert.True(listenerToward > 1f);
        Assert.True(listenerAway < 1f);
        Assert.True(both > toward);
    }

    [Fact]
    public void Doppler_AtExactlyTheSpeedOfSound_StaysFiniteAndLarge()
    {
        using var world = new World();
        var tracer = Tracer(world, relativistic: false);
        tracer.UpdateSceneCache();

        float sonic = Doppler(tracer, Vector3.Zero, new Vector3(SoundSpeed, 0, 0), new Vector3(10, 0, 0), Vector3.Zero);
        Assert.True(float.IsFinite(sonic));
        Assert.Equal(SoundSpeed / 0.01f, sonic, 1f);
    }

    [Fact]
    public void Doppler_JustAboveTheSpeedOfSound_IsALargeNegativePitch()
    {
        using var world = new World();
        var tracer = Tracer(world, relativistic: false);
        tracer.UpdateSceneCache();

        float supersonic = Doppler(
            tracer,
            Vector3.Zero,
            new Vector3(SoundSpeed + 0.005f, 0, 0),
            new Vector3(10, 0, 0),
            Vector3.Zero);

        Assert.Equal(-SoundSpeed / 0.01f, supersonic, 1f);
        Assert.True(supersonic < 0f);
    }

    [Fact(Skip = "BUG: a source moving faster than sound produces a negative DopplerShift (about -34300). The epsilon guard keeps the sign of (c - sourceSpeed). Expected: pitch stays finite and positive.")]
    public void Doppler_AboveTheSpeedOfSound_StaysPositive()
    {
        using var world = new World();
        var tracer = Tracer(world, relativistic: false);
        tracer.UpdateSceneCache();
        float shift = Doppler(
            tracer,
            Vector3.Zero,
            new Vector3(SoundSpeed + 1f, 0, 0),
            new Vector3(10, 0, 0),
            Vector3.Zero);
        Assert.True(shift > 0f);
        Assert.True(float.IsFinite(shift));
    }

    [Fact]
    public void Occlusion_DropsTheDirectPath_OrReplacesItWithTransmission()
    {
        using var world = new World();
        var wall = AddSurface(world, new Vector3(10, 0, 0), AcousticMaterial.EventHorizon);

        var blocked = Tracer(world, transmission: false);
        blocked.UpdateSceneCache();
        Assert.Empty(blocked.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));

        var open = Tracer(world, transmission: true);
        open.UpdateSceneCache();
        var dead = Assert.Single(open.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        Assert.Equal(PathType.Transmission, dead.Type);
        Assert.False(dead.IsDirect);
        Assert.Equal(0f, dead.FinalResponse.Average, 1e-5);

        ref var carpetSurface = ref world.GetComponent<AcousticSurfaceComponent>(wall);
        carpetSurface.Material = AcousticMaterial.Carpet;
        var carpetTracer = Tracer(world, transmission: true);
        carpetTracer.UpdateSceneCache();
        var carpet = Assert.Single(carpetTracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        Assert.Equal(0f, carpet.FinalResponse.Average, 1e-5);
        Assert.Equal(PathType.Transmission, carpet.Type);
    }

    [Fact]
    public void Transmission_ThroughAirKeepsEnergy_AndConcreteCutsHighsMoreThanLows()
    {
        using var world = new World();
        var wall = AddSurface(world, new Vector3(10, 0, 0), AcousticMaterial.Air);
        var tracer = Tracer(world, transmission: true);
        tracer.UpdateSceneCache();

        var air = Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        float attenuation = 1f / (1f + 400f * 0.01f);
        Assert.Equal(PathType.Transmission, air.Type);
        Assert.False(air.IsDirect);
        Assert.Equal(attenuation, air.FinalResponse.Average, 1e-4);
        Assert.Equal(20f, air.TotalDistance, 1e-3);

        ref var wallSurface = ref world.GetComponent<AcousticSurfaceComponent>(wall);
        wallSurface.Material = AcousticMaterial.Concrete;
        var concreteTracer = Tracer(world, transmission: true);
        concreteTracer.UpdateSceneCache();
        var concrete = Assert.Single(concreteTracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        Assert.True(concrete.FinalResponse.Band8kHz < concrete.FinalResponse.Band63Hz);
        Assert.True(concrete.FinalResponse.Average < air.FinalResponse.Average);
        Assert.True(concrete.FinalResponse.Average > 0f);
    }

    [Fact]
    public void Transmission_ThroughTwoWalls_IsQuieterThanOneWall()
    {
        using var oneWorld = new World();
        AddSurface(oneWorld, new Vector3(20, 0, 0), AcousticMaterial.Concrete);
        var one = Tracer(oneWorld, transmission: true);
        one.UpdateSceneCache();
        float oneLoss = Assert.Single(one.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(40, 0, 0), Vector3.Zero, 0)).FinalResponse.Average;

        using var twoWorld = new World();
        AddSurface(twoWorld, new Vector3(10, 0, 0), AcousticMaterial.Concrete);
        AddSurface(twoWorld, new Vector3(25, 0, 0), AcousticMaterial.Concrete);
        var two = Tracer(twoWorld, transmission: true);
        two.UpdateSceneCache();
        float twoLoss = Assert.Single(two.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(40, 0, 0), Vector3.Zero, 0)).FinalResponse.Average;

        Assert.True(twoLoss < oneLoss);
        Assert.True(twoLoss > 0f);
    }

    [Fact]
    public void NullMaterial_StillCountsAsOcclusion_ButDoesNotAbsorb()
    {
        using var world = new World();
        var wall = world.CreateEntity();
        world.AddComponent(wall, new TransformComponent(new Vector3(10, 0, 0)));
        world.AddComponent(wall, new AcousticSurfaceComponent { Material = null! });

        var tracer = Tracer(world, transmission: true);
        tracer.UpdateSceneCache();
        var path = Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        Assert.Equal(PathType.Transmission, path.Type);
        Assert.False(path.IsDirect);
        Assert.Equal(1f / (1f + 400f * 0.01f), path.FinalResponse.Average, 1e-4);

        var blocked = Tracer(world, transmission: false);
        blocked.UpdateSceneCache();
        Assert.Empty(blocked.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
    }

    [Fact]
    public void SceneCache_IgnoresSurfacesAndGravityUntilItIsRebuilt()
    {
        using var world = new World();
        var wall = AddSurface(world, new Vector3(100, 0, 0), AcousticMaterial.EventHorizon);
        var tracer = Tracer(world, transmission: false);
        tracer.UpdateSceneCache();

        Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));

        world.GetComponent<TransformComponent>(wall).Position = new Vector3(10, 0, 0);
        Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));

        tracer.UpdateSceneCache();
        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));

        world.RemoveComponent<AcousticSurfaceComponent>(wall);
        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        tracer.UpdateSceneCache();
        Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
    }

    [Fact]
    public void Gravity_RedshiftsAndShortensDelay_OnlyForRelativisticSources()
    {
        using var world = new World();
        var hole = world.CreateEntity();
        world.AddComponent(hole, new TransformComponent(Vector3.Zero));
        world.AddComponent(hole, GravitySourceComponent.CreateBlackHole(1f)); // rs = 2

        var flat = Tracer(world, relativistic: false, maxDistance: 50f);
        flat.UpdateSceneCache();
        var unaffected = Assert.Single(flat.TracePaths(new Vector3(4, 0, 0), Vector3.Zero, new Vector3(0, 4, 0), Vector3.Zero, 0));
        float separation = Vector3.Distance(new Vector3(4, 0, 0), new Vector3(0, 4, 0));
        Assert.Equal(1f, unaffected.GravitationalShift, 1e-5);
        Assert.Equal(separation / SoundSpeed, unaffected.TotalTime, 1e-5);

        var bent = Tracer(world, relativistic: true, maxDistance: 50f);
        // Cache starts empty, so the hole is invisible until UpdateSceneCache.
        var beforeCache = Assert.Single(bent.TracePaths(new Vector3(4, 0, 0), Vector3.Zero, new Vector3(0, 4, 0), Vector3.Zero, 0));
        Assert.Equal(1f, beforeCache.GravitationalShift, 1e-5);

        bent.UpdateSceneCache();
        var shifted = Assert.Single(bent.TracePaths(new Vector3(4, 0, 0), Vector3.Zero, new Vector3(0, 4, 0), Vector3.Zero, 0));
        float dilation = MathF.Sqrt(1f - 2f / 4f);
        Assert.Equal(dilation, shifted.GravitationalShift, 1e-4);
        Assert.Equal(separation / SoundSpeed * dilation, shifted.TotalTime, 1e-4);
        Assert.Equal(shifted.DopplerShift * shifted.GravitationalShift, shifted.TotalPitchShift, 1e-5);
        Assert.True(shifted.TotalTime < unaffected.TotalTime);

        // Both endpoints sit inside rs * 1.01, so the clamp collapses the shift to 0.001.
        var horizon = Assert.Single(bent.TracePaths(new Vector3(2f, 0, 0), Vector3.Zero, new Vector3(0, 2f, 0), Vector3.Zero, 0));
        Assert.Equal(0.001f, horizon.GravitationalShift, 1e-6);

        ref var gravity = ref world.GetComponent<GravitySourceComponent>(hole);
        gravity.GravityType = GravityType.Newtonian;
        gravity.AffectsLight = true;
        bent.UpdateSceneCache();
        var newton = Assert.Single(bent.TracePaths(new Vector3(4, 0, 0), Vector3.Zero, new Vector3(0, 4, 0), Vector3.Zero, 0));
        Assert.Equal(1f, newton.GravitationalShift, 1e-5);

        gravity.GravityType = GravityType.Uniform;
        gravity.AffectsLight = true;
        bent.UpdateSceneCache();
        var uniform = Assert.Single(bent.TracePaths(new Vector3(4, 0, 0), Vector3.Zero, new Vector3(0, 4, 0), Vector3.Zero, 0));
        Assert.Equal(1f, uniform.GravitationalShift, 1e-5);
    }

    [Fact]
    public void KerrOutsideItsHorizonButInsideTheSchwarzschildRadius_IsTreatedAsSwallowed()
    {
        using var world = new World();
        var hole = world.CreateEntity();
        world.AddComponent(hole, new TransformComponent(Vector3.Zero));
        // rs = 20. Outer Kerr horizon is about 10.63, so r = 15 is still outside the real horizon.
        world.AddComponent(hole, GravitySourceComponent.CreateRotatingBlackHole(10f, 0.998f, Vector3.UnitY));

        var tracer = Tracer(world, relativistic: true, maxDistance: 80f);
        tracer.UpdateSceneCache();
        var path = Assert.Single(tracer.TracePaths(new Vector3(15, 0, 0), Vector3.Zero, new Vector3(0, 15, 0), Vector3.Zero, 0));
        Assert.Equal(0.001f, path.GravitationalShift, 1e-5);
    }

    [Fact(Skip = "BUG: Kerr audio uses SchwarzschildRadius (2M) for the horizon clamp. A listener at r=15 around a spin-0.998 mass-10 hole is outside the outer horizon (~10.6) but GravitationalShift collapses to 0.001. Expected: a finite redshift greater than 0.001.")]
    public void KerrOutsideItsHorizon_KeepsAFiniteRedshift()
    {
        using var world = new World();
        var hole = world.CreateEntity();
        world.AddComponent(hole, new TransformComponent(Vector3.Zero));
        world.AddComponent(hole, GravitySourceComponent.CreateRotatingBlackHole(10f, 0.998f, Vector3.UnitY));
        var tracer = Tracer(world, relativistic: true, maxDistance: 80f);
        tracer.UpdateSceneCache();
        var path = Assert.Single(tracer.TracePaths(new Vector3(15, 0, 0), Vector3.Zero, new Vector3(0, 15, 0), Vector3.Zero, 0));
        Assert.True(path.GravitationalShift > 0.05f);
        Assert.True(path.GravitationalShift < 1f);
    }

    [Fact]
    public void Reflections_ReachTheListener_AndCarryAbsorptionDopplerAndDelayClass()
    {
        using var world = new World();
        // Sphere radius is hardcoded to 5. Center just beyond that radius, on the opposite
        // side of the listener, so a large fraction of rays hit and can still see the listener.
        AddSurface(world, new Vector3(0, 0, 5.05f), AcousticMaterial.Concrete);
        var tracer = Tracer(world, relativistic: false, maxBounces: 4, energy: 0.001f);
        tracer.UpdateSceneCache();

        Vector3 listener = new(0, 0, -10);
        var early = CollectReflections(tracer, Vector3.Zero, listener, new Vector3(0, 0, -25), Vector3.Zero);
        Assert.Contains(early, path => path.Type == PathType.EarlyReflection);
        Assert.All(early, path =>
        {
            var expectedType = path.TotalTime > 0.08f ? PathType.LateReflection : PathType.EarlyReflection;
            Assert.Equal(expectedType, path.Type);
            Assert.False(path.IsDirect);
            Assert.NotEmpty(path.Hits);
            Assert.Equal("Concrete", path.Hits[0].Material!.Name);
            Assert.True(path.TotalDistance > 10f);
            Assert.True(path.FinalResponse.Average > 0f);
            Assert.Equal(SoundSpeed / (SoundSpeed - 25f), path.DopplerShift, 1e-3);
        });

        var direct = tracer.TracePaths(Vector3.Zero, new Vector3(0, 0, -25), listener, Vector3.Zero, 0)[0];
        Assert.True(early[0].FinalResponse.Average < direct.FinalResponse.Average);

        var late = CollectReflections(tracer, Vector3.Zero, new Vector3(0, 0, -40), Vector3.Zero, Vector3.Zero);
        Assert.NotEmpty(late);
        Assert.Contains(late, path => path.Type == PathType.LateReflection && path.TotalTime > 0.08f);
    }

    [Fact]
    public void FoamReflections_AreQuieterThanConcrete_AndAnEventHorizonReflectsNothing()
    {
        using var concreteWorld = new World();
        AddSurface(concreteWorld, new Vector3(0, 0, 5.05f), AcousticMaterial.Concrete);
        var concrete = Tracer(concreteWorld, relativistic: false);
        concrete.UpdateSceneCache();
        float concreteLevel = CollectReflections(concrete, Vector3.Zero, new Vector3(0, 0, -8), Vector3.Zero, Vector3.Zero)
            .Average(p => p.FinalResponse.Average);

        using var foamWorld = new World();
        AddSurface(foamWorld, new Vector3(0, 0, 5.05f), AcousticMaterial.AcousticFoam);
        var foam = Tracer(foamWorld, relativistic: false);
        foam.UpdateSceneCache();
        float foamLevel = CollectReflections(foam, Vector3.Zero, new Vector3(0, 0, -8), Vector3.Zero, Vector3.Zero)
            .Average(p => p.FinalResponse.Average);

        using var holeWorld = new World();
        AddSurface(holeWorld, new Vector3(0, 0, 5.05f), AcousticMaterial.EventHorizon);
        var hole = Tracer(holeWorld, relativistic: false);
        hole.UpdateSceneCache();
        var swallowed = CollectReflections(hole, Vector3.Zero, new Vector3(0, 0, -8), Vector3.Zero, Vector3.Zero);

        Assert.True(foamLevel < concreteLevel);
        Assert.True(concreteLevel > 0f);
        Assert.NotEmpty(swallowed);
        Assert.All(swallowed, path => Assert.Equal(0f, path.FinalResponse.Average, 1e-5));
    }

    [Fact]
    public void BounceLimitAndEnergyThreshold_StopReflectionsBeforeTheyStart()
    {
        using var world = new World();
        AddSurface(world, new Vector3(0, 0, 5.05f), AcousticMaterial.Concrete);

        var noBounces = Tracer(world, relativistic: false, maxBounces: 0);
        noBounces.UpdateSceneCache();
        var onlyDirect = noBounces.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(0, 0, -10), Vector3.Zero, rayCount: 200);
        Assert.DoesNotContain(onlyDirect, p => p.Type is PathType.EarlyReflection or PathType.LateReflection);
        Assert.Contains(onlyDirect, p => p.IsDirect);

        var noEnergy = Tracer(world, relativistic: false, maxBounces: 8, energy: 1f);
        noEnergy.UpdateSceneCache();
        var starved = noEnergy.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(0, 0, -10), Vector3.Zero, rayCount: 200);
        Assert.DoesNotContain(starved, p => p.Type is PathType.EarlyReflection or PathType.LateReflection);
    }

    [Fact]
    public void ListenerInsideASphere_IsNotReachedByAReflection()
    {
        using var world = new World();
        AddSurface(world, new Vector3(0, 0, 12), AcousticMaterial.Concrete);
        var tracer = Tracer(world, transmission: true, relativistic: false, maxBounces: 4);
        tracer.UpdateSceneCache();

        // Listener at z=12 is inside the radius-5 sphere. Rays can bounce, but the shell occludes the listener.
        var paths = tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(0, 0, 12), Vector3.Zero, rayCount: 64);
        Assert.Contains(paths, p => p.Type == PathType.Transmission);
    }

    [Fact]
    public void SurfaceFlagsAndThicknessOverride_AreIgnoredByTheRaytracer()
    {
        using var world = new World();
        var wall = AddSurface(world, new Vector3(10, 0, 0), AcousticMaterial.Glass);
        ref var surface = ref world.GetComponent<AcousticSurfaceComponent>(wall);
        surface.AffectsDirectSound = false;
        surface.AllowsTransmission = false;
        surface.ThicknessOverride = 50f;

        var tracer = Tracer(world, transmission: true);
        tracer.UpdateSceneCache();
        var path = Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        Assert.Equal(PathType.Transmission, path.Type);
        Assert.True(path.FinalResponse.Average > 0f);

        surface.ThicknessOverride = 0f;
        tracer.UpdateSceneCache();
        var withoutOverride = Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        Assert.Equal(path.FinalResponse.Average, withoutOverride.FinalResponse.Average, 1e-6);

        surface.GeneratesReflections = false;
        var mirror = AddSurface(world, new Vector3(0, 0, 5.05f), AcousticMaterial.Concrete);
        ref var mirrorSurface = ref world.GetComponent<AcousticSurfaceComponent>(mirror);
        mirrorSurface.GeneratesReflections = false;
        tracer.UpdateSceneCache();
        Assert.NotEmpty(CollectReflections(tracer, Vector3.Zero, new Vector3(0, 0, -8), Vector3.Zero, Vector3.Zero));
    }

    [Fact(Skip = "BUG: AcousticSurfaceComponent.AffectsDirectSound, AllowsTransmission, GeneratesReflections, and ThicknessOverride are never read. A surface with AffectsDirectSound false still occludes, AllowsTransmission false still transmits, GeneratesReflections false still reflects, and ThicknessOverride does not change transmission loss. Expected: those fields control occlusion, transmission, reflections, and thickness.")]
    public void SurfaceFlags_ControlOcclusionTransmissionAndReflections()
    {
        using var world = new World();
        var wall = AddSurface(world, new Vector3(10, 0, 0), AcousticMaterial.Glass);
        world.GetComponent<AcousticSurfaceComponent>(wall).AffectsDirectSound = false;
        var tracer = Tracer(world, transmission: true);
        tracer.UpdateSceneCache();
        var path = Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 0));
        Assert.True(path.IsDirect);
        Assert.Equal(PathType.Direct, path.Type);
    }

    [Fact]
    public void EnableDiffraction_DoesNotProduceDiffractionPaths()
    {
        using var world = new World();
        AddSurface(world, new Vector3(10, 0, 0), AcousticMaterial.Concrete);
        var settings = new AcousticQualitySettings
        {
            RaysPerSource = 32,
            MaxBounces = 4,
            MaxDistance = 100f,
            EnergyThreshold = 0.001f,
            EnableDiffraction = true,
            EnableTransmission = false,
            EnableRelativisticEffects = false,
            SpeedOfSound = SoundSpeed
        };
        var tracer = new AcousticRaytracer(world, settings);
        tracer.UpdateSceneCache();
        var paths = tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 32);
        Assert.DoesNotContain(paths, p => p.Type == PathType.Diffraction);
        Assert.True(tracer.Settings.EnableDiffraction);
    }

    [Fact(Skip = "BUG: AcousticQualitySettings.EnableDiffraction is never read. With diffraction enabled and the direct path blocked, no PathType.Diffraction paths are produced. Expected: sound diffracts around the occluding sphere.")]
    public void EnableDiffraction_BendsSoundAroundAnOccluder()
    {
        using var world = new World();
        AddSurface(world, new Vector3(10, 0, 0), AcousticMaterial.Concrete);
        var settings = new AcousticQualitySettings
        {
            RaysPerSource = 64,
            MaxBounces = 4,
            MaxDistance = 100f,
            EnableDiffraction = true,
            EnableTransmission = false,
            EnableRelativisticEffects = false,
            SpeedOfSound = SoundSpeed
        };
        var tracer = new AcousticRaytracer(world, settings);
        tracer.UpdateSceneCache();
        var paths = tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero, 64);
        Assert.Contains(paths, p => p.Type == PathType.Diffraction && p.FinalResponse.Average > 0f);
    }

    private static AcousticRaytracer Tracer(
        World world,
        bool transmission = true,
        bool relativistic = false,
        float maxDistance = 200f,
        int maxBounces = 4,
        float energy = 0.001f)
    {
        return new AcousticRaytracer(world, new AcousticQualitySettings
        {
            RaysPerSource = 0,
            MaxBounces = maxBounces,
            MaxDistance = maxDistance,
            EnergyThreshold = energy,
            EnableTransmission = transmission,
            EnableDiffraction = false,
            EnableRelativisticEffects = relativistic,
            SpeedOfSound = SoundSpeed,
            UpdateRate = 30f
        });
    }

    private static Entity AddSurface(World world, Vector3 position, AcousticMaterial material)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        world.AddComponent(entity, AcousticSurfaceComponent.Create(material));
        return entity;
    }

    private static float Doppler(AcousticRaytracer tracer, Vector3 source, Vector3 sourceVelocity, Vector3 listener, Vector3 listenerVelocity)
    {
        var path = Assert.Single(tracer.TracePaths(source, sourceVelocity, listener, listenerVelocity, 0));
        return path.DopplerShift;
    }

    private static List<AcousticPath> CollectReflections(
        AcousticRaytracer tracer,
        Vector3 source,
        Vector3 listener,
        Vector3 sourceVelocity,
        Vector3 listenerVelocity)
    {
        var found = new List<AcousticPath>();
        for (int attempt = 0; attempt < 8 && found.Count == 0; attempt++)
        {
            var paths = tracer.TracePaths(source, sourceVelocity, listener, listenerVelocity, rayCount: 160);
            found.AddRange(paths.Where(p => p.Type is PathType.EarlyReflection or PathType.LateReflection));
        }

        Assert.NotEmpty(found);
        return found;
    }
}
