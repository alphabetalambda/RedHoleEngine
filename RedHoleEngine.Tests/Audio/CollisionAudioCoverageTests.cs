using System.Numerics;
using System.Reflection;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Tests.Audio;

public class CollisionAudioCoverageTests
{
    [Fact]
    public void Initialize_RejectsNullDependencies_AndCopiesLibraryPaths()
    {
        using var world = new World();
        var system = world.AddSystem(new CollisionAudioSystem());
        using var engine = Engine(world);
        var physics = new PhysicsWorld();

        var physicsException = Assert.Throws<ArgumentNullException>(() => system.Initialize(null!, engine));
        var engineException = Assert.Throws<ArgumentNullException>(() => system.Initialize(physics, null!));
        Assert.Equal("physicsWorld", physicsException.ParamName);
        Assert.Equal("audioEngine", engineException.ParamName);

        var routed = CollisionSoundConfig.Default;
        routed.BasePath = "sfx/hits/";
        routed.Extension = ".ogg";
        var routedSystem = world.AddSystem(new CollisionAudioSystem(routed));
        routedSystem.Initialize(physics, engine);
        Assert.Equal("sfx/hits/metal_metal_impact.ogg", routedSystem.SoundLibrary.GetImpactSound(SurfaceType.Metal, SurfaceType.Metal));
        Assert.Equal(1, HandlerCount(physics, "OnCollisionEnter"));
        Assert.Equal(1, HandlerCount(physics, "OnCollisionStay"));
    }

    [Fact]
    public void Impact_UsesSqrtVelocityMassAndComponentMultipliers()
    {
        using var harness = new Harness(maxVolumeVelocity: 10f, minImpact: 0.5f, cooldown: 0.05f, maxSounds: 8);
        var lightA = harness.Entity(SurfaceType.Metal, volume: 1f, pitch: 1f);
        var lightB = harness.Entity(SurfaceType.Stone, volume: 1f, pitch: 1f);

        harness.Enter(lightA, lightB, impactSpeed: 2.5f);
        var voice = Assert.Single(harness.Backend.PlayedVoices);
        Assert.Equal(0.5f, harness.Backend.Volumes[voice.Id], 1e-4);
        Assert.Contains("metal_stone_impact", harness.Backend.LoadedPaths[0]);
        float lightPitch = ExpectedPitch(impactSpeed: 2.5f, combinedMass: 2f, pitchMultiplier: 1f);
        Assert.Equal(lightPitch, harness.Backend.Pitches[voice.Id], 1e-4);

        var heavyA = harness.Entity(SurfaceType.Metal, volume: 0.5f, pitch: 1f, mass: 50f);
        var heavyB = harness.Entity(SurfaceType.Metal, volume: 0.5f, pitch: 1f, mass: 50f);
        harness.Enter(heavyA, heavyB, impactSpeed: 10f);
        var heavy = harness.Backend.PlayedVoices[^1];
        Assert.Equal(0.25f, harness.Backend.Volumes[heavy.Id], 1e-4);
        Assert.True(float.IsFinite(harness.Backend.Pitches[heavy.Id]));
        Assert.Contains("metal_metal_impact", harness.Backend.LoadedPaths[^1]);

        var customA = harness.Entity(SurfaceType.Wood, volume: 1f, pitch: 2f, customPath: "hits/a.wav");
        var customB = harness.Entity(SurfaceType.Wood, volume: 1f, pitch: 2f, customPath: "hits/b.wav");
        harness.Enter(customA, customB, impactSpeed: 10f);
        Assert.Equal("hits/a.wav", harness.Backend.LoadedPaths[^1]);
        Assert.Equal(2f, harness.Backend.Pitches[harness.Backend.PlayedVoices[^1].Id], 1e-4);

        var quiet = harness.Entity(SurfaceType.Rubber, volume: 0.1f, pitch: 0.1f);
        var other = harness.Entity(SurfaceType.Default, volume: 1f, pitch: 1f);
        harness.Enter(quiet, other, impactSpeed: 10f, massA: 1f, massB: 1f);
        Assert.Equal(0.5f, harness.Backend.Pitches[harness.Backend.PlayedVoices[^1].Id], 1e-4);
        Assert.Equal(0.1f, harness.Backend.Volumes[harness.Backend.PlayedVoices[^1].Id], 1e-4);
    }

    [Fact]
    public void Impact_RespectsMinVelocityCooldownMaxSoundsAndContactFallback()
    {
        using var harness = new Harness(maxVolumeVelocity: 10f, minImpact: 0.5f, cooldown: 0.05f, maxSounds: 2);
        var a = harness.Entity(SurfaceType.Glass, 1f, 1f);
        var b = harness.Entity(SurfaceType.Concrete, 1f, 1f);

        Assert.Empty(harness.Backend.PlayedVoices);
        harness.Enter(a, b, impactSpeed: 0.49f);
        Assert.Empty(harness.Backend.PlayedVoices);

        ImpactData? reported = null;
        harness.System.OnImpactSoundPlayed += (impact, _) => reported = impact;
        harness.Enter(a, b, impactSpeed: 0.5f, contact: new Vector3(2, 3, 4));
        Assert.Single(harness.Backend.PlayedVoices);
        Assert.Equal(new Vector3(2, 3, 4), harness.Backend.Positions[harness.Backend.PlayedVoices[0].Id]);
        Assert.Equal(new Vector3(2, 3, 4), reported!.Value.Position);
        Assert.Equal(0.5f, reported.Value.ImpactVelocity, 1e-4);
        Assert.Contains("glass_impact", harness.Backend.LoadedPaths[0]);

        harness.Enter(a, b, impactSpeed: 5f);
        Assert.Single(harness.Backend.PlayedVoices);

        harness.System.Update(0.06f);
        harness.Enter(b, a, impactSpeed: 5f);
        Assert.Equal(2, harness.Backend.PlayedVoices.Count);

        var c = harness.Entity(SurfaceType.Wood, 1f, 1f);
        var d = harness.Entity(SurfaceType.Wood, 1f, 1f);
        var e = harness.Entity(SurfaceType.Stone, 1f, 1f);
        var f = harness.Entity(SurfaceType.Stone, 1f, 1f);
        harness.Enter(c, d, impactSpeed: 4f);
        harness.Enter(e, f, impactSpeed: 4f);
        Assert.Equal(3, harness.Backend.PlayedVoices.Count);

        harness.System.Update(0.01f);
        var g = harness.Entity(SurfaceType.Plastic, 1f, 1f);
        var h = harness.Entity(SurfaceType.Default, 1f, 1f);
        harness.Enter(g, h, impactSpeed: 4f);
        Assert.Equal(4, harness.Backend.PlayedVoices.Count);
        Assert.Contains("plastic_impact", harness.Backend.LoadedPaths[^1]);
    }

    [Fact]
    public void EmptyContacts_UseTheBodyMidpoint_AndMissingEntitiesStayDefault()
    {
        using var harness = new Harness(10f, 0.1f, 0.01f, 8);
        var bodyA = harness.Body(entityId: 900, mass: 1f, dynamic: true);
        var bodyB = harness.Body(entityId: 901, mass: 1f, dynamic: true);
        bodyA.Position = new Vector3(0, 0, 0);
        bodyB.Position = new Vector3(0, 4, 0);
        bodyB.LinearVelocity = new Vector3(0, -4, 0);

        var manifold = new CollisionManifold { BodyA = bodyA, BodyB = bodyB, ColliderA = new SphereCollider(0.5f), ColliderB = new SphereCollider(0.5f) };
        harness.RaiseEnter(new CollisionEvent
        {
            BodyA = bodyA,
            BodyB = bodyB,
            ColliderA = manifold.ColliderA,
            ColliderB = manifold.ColliderB,
            Manifold = manifold
        });

        var voice = Assert.Single(harness.Backend.PlayedVoices);
        Assert.Equal(new Vector3(0, 2, 0), harness.Backend.Positions[voice.Id]);
        Assert.Equal(MathF.Sqrt(0.4f), harness.Backend.Volumes[voice.Id], 1e-4);
        Assert.Contains("default_impact", harness.Backend.LoadedPaths[0]);
    }

    [Fact]
    public void DisabledComponent_StillAppliesCustomPathAndMultipliers()
    {
        using var harness = new Harness(10f, 0.1f, 0.01f, 8);
        var disabled = harness.Entity(SurfaceType.Metal, volume: 0.5f, pitch: 1f, customPath: "custom/disabled.wav", enabled: false);
        var other = harness.Entity(SurfaceType.Stone, volume: 1f, pitch: 1f);
        harness.Enter(disabled, other, impactSpeed: 10f);

        Assert.Equal("custom/disabled.wav", harness.Backend.LoadedPaths.Single());
        Assert.Equal(0.5f, harness.Backend.Volumes[harness.Backend.PlayedVoices[0].Id], 1e-4);
        ref var component = ref harness.World.GetComponent<CollisionSoundComponent>(disabled);
        Assert.Equal(float.NegativeInfinity, component.LastImpactTime);
        Assert.Equal(-1, component.LastCollidedEntityId);
    }

    [Fact(Skip = "BUG: CollisionSoundComponent.Enabled does not suppress playback. A disabled component still contributes CustomSoundPath and VolumeMultiplier, and LastImpactTime/LastCollidedEntityId stay at their defaults. Expected: disabled components are ignored, so this impact uses the stone/default library path at full volume and records the impact time.")]
    public void DisabledComponent_IsIgnored()
    {
        using var harness = new Harness(10f, 0.1f, 0.01f, 8);
        var disabled = harness.Entity(SurfaceType.Metal, volume: 0.5f, pitch: 1f, customPath: "custom/disabled.wav", enabled: false);
        var other = harness.Entity(SurfaceType.Stone, volume: 1f, pitch: 1f);
        harness.Enter(disabled, other, impactSpeed: 10f);

        Assert.DoesNotContain("custom/disabled.wav", harness.Backend.LoadedPaths);
        Assert.Equal(1f, harness.Backend.Volumes[harness.Backend.PlayedVoices[0].Id], 1e-3);
        Assert.Equal(0f, harness.World.GetComponent<CollisionSoundComponent>(disabled).LastImpactTime);
    }

    [Fact]
    public void MinImpactVelocityOverride_IsNotConsulted()
    {
        using var harness = new Harness(10f, minImpact: 0.5f, cooldown: 0.01f, maxSounds: 4);
        var entity = harness.Entity(SurfaceType.Metal, 1f, 1f, minImpactOverride: 100f);
        var other = harness.Entity(SurfaceType.Metal, 1f, 1f);
        harness.Enter(entity, other, impactSpeed: 5f);
        Assert.Single(harness.Backend.PlayedVoices);
    }

    [Fact(Skip = "BUG: CollisionSoundComponent.MinImpactVelocityOverride is never read. Expected: an override of 100 rejects a 5 m/s impact. Actual: only CollisionSoundConfig.MinImpactVelocity is applied, so the impact plays.")]
    public void MinImpactVelocityOverride_RaisesTheThreshold()
    {
        using var harness = new Harness(10f, minImpact: 0.5f, cooldown: 0.01f, maxSounds: 4);
        var entity = harness.Entity(SurfaceType.Metal, 1f, 1f, minImpactOverride: 100f);
        var other = harness.Entity(SurfaceType.Metal, 1f, 1f);
        harness.Enter(entity, other, impactSpeed: 5f);
        Assert.Empty(harness.Backend.PlayedVoices);
    }

    [Fact]
    public void Sliding_PlaysBySpeed_AndIgnoresThePerFrameCap()
    {
        using var harness = new Harness(10f, 0.1f, 0.01f, maxSounds: 1, sliding: true, minSlide: 0.3f);
        var metal = harness.Entity(SurfaceType.Metal, 1f, 1f);
        var wood = harness.Entity(SurfaceType.Wood, 1f, 1f);

        int guard = 0;
        while (harness.Backend.PlayedVoices.Count < 2 && guard++ < 8000)
            harness.Stay(metal, wood, slidingSpeed: 4f);

        Assert.True(harness.Backend.PlayedVoices.Count >= 2);
        Assert.Contains(harness.Backend.LoadedPaths, path => path.Contains("metal_slide"));
        var slide = harness.Backend.PlayedVoices[0];
        Assert.Equal(0.4f, harness.Backend.Volumes[slide.Id], 1e-4); // min(4/5, 1) * 0.5
        Assert.Equal(1.12f, harness.Backend.Pitches[slide.Id], 1e-4); // 0.8 + 0.8 * 0.4

        int before = harness.Backend.PlayedVoices.Count;
        for (int i = 0; i < 200; i++)
            harness.Stay(metal, wood, slidingSpeed: 0.05f);
        Assert.Equal(before, harness.Backend.PlayedVoices.Count);
    }

    [Fact(Skip = "BUG: MaxSoundsPerFrame is only checked for collision enter. HandleCollisionStay can play more one-shots than the cap in a single frame. Expected: sliding stops once the frame cap is reached.")]
    public void Sliding_RespectsMaxSoundsPerFrame()
    {
        using var harness = new Harness(10f, 0.1f, 0.01f, maxSounds: 1, sliding: true, minSlide: 0.3f);
        var metal = harness.Entity(SurfaceType.Metal, 1f, 1f);
        var wood = harness.Entity(SurfaceType.Wood, 1f, 1f);
        int guard = 0;
        while (harness.Backend.PlayedVoices.Count < 1 && guard++ < 8000)
            harness.Stay(metal, wood, slidingSpeed: 4f);
        Assert.Equal(1, harness.Backend.PlayedVoices.Count);
    }

    [Fact]
    public void Sliding_CanBeDisabled()
    {
        using var harness = new Harness(10f, 0.1f, 0.01f, 8, sliding: false, minSlide: 0.3f);
        var a = harness.Entity(SurfaceType.Metal, 1f, 1f);
        var b = harness.Entity(SurfaceType.Stone, 1f, 1f);
        for (int i = 0; i < 300; i++)
            harness.Stay(a, b, slidingSpeed: 5f);
        Assert.Empty(harness.Backend.PlayedVoices);

        harness.System.FixedUpdate(0.02f);
        Assert.Empty(harness.Backend.PlayedVoices);
    }

    [Fact]
    public void UninitializedEngine_DropsImpacts_AndDestroyUnsubscribes()
    {
        using var world = new World();
        var system = world.AddSystem<CollisionAudioSystem>();
        var physics = new PhysicsWorld();
        var backend = new FakeAudioBackend();
        using var engine = new AudioEngine(world, backend, AcousticQualitySettings.Low);
        system.Initialize(physics, engine);

        var a = world.CreateEntity();
        var b = world.CreateEntity();
        world.AddCollisionSound(a, SurfaceType.Metal);
        world.AddCollisionSound(b, SurfaceType.Metal);
        Raise(system, "HandleCollisionEnter", Impact(a, b, 5f));
        Assert.Empty(backend.PlayedVoices);

        engine.Initialize();
        Raise(system, "HandleCollisionEnter", Impact(a, b, 5f));
        Assert.Single(backend.PlayedVoices);

        system.OnDestroy();
        Assert.Null(EventDelegate(physics, "OnCollisionEnter"));
        Assert.Null(EventDelegate(physics, "OnCollisionStay"));
    }

    [Fact]
    public void SecondInitialize_StacksHandlers()
    {
        using var world = new World();
        var system = world.AddSystem<CollisionAudioSystem>();
        var physics = new PhysicsWorld();
        using var engine = Engine(world);
        var config = system.Config;
        config.Cooldown = 0f;
        config.PitchVariation = 0f;
        system.Config = config;
        system.Initialize(physics, engine);
        system.Initialize(physics, engine);
        Assert.Equal(2, HandlerCount(physics, "OnCollisionEnter"));

        var enter = EventDelegate(physics, "OnCollisionEnter");
        enter!.DynamicInvoke(Impact(world.CreateEntity(), world.CreateEntity(), 6f));
        Assert.Equal(2, ((FakeAudioBackend)engine.Backend).PlayedVoices.Count);

        system.OnDestroy();
        Assert.Equal(1, HandlerCount(physics, "OnCollisionEnter"));
    }

    [Fact(Skip = "BUG: CollisionAudioSystem.Initialize subscribes again every call, so one collision plays two one-shots, and OnDestroy only removes one of the delegates. Expected: Initialize is idempotent and OnDestroy detaches every handler.")]
    public void Initialize_IsIdempotent()
    {
        using var world = new World();
        var system = world.AddSystem<CollisionAudioSystem>();
        var physics = new PhysicsWorld();
        using var engine = Engine(world);
        system.Initialize(physics, engine);
        system.Initialize(physics, engine);
        Assert.Equal(1, HandlerCount(physics, "OnCollisionEnter"));
        system.OnDestroy();
        Assert.Null(EventDelegate(physics, "OnCollisionEnter"));
    }

    [Fact]
    public void CooldownBookkeeping_ExpiresOldPairs()
    {
        using var harness = new Harness(10f, 0.1f, cooldown: 0.05f, maxSounds: 4);
        var a = harness.Entity(SurfaceType.Metal, 1f, 1f);
        var b = harness.Entity(SurfaceType.Wood, 1f, 1f);
        harness.Enter(a, b, impactSpeed: 3f);
        Assert.Single(harness.Cooldowns);

        for (int i = 0; i < 20; i++)
            harness.System.FixedUpdate(1f);
        Assert.Single(harness.Cooldowns);

        harness.System.Update(0.05f * 11f);
        for (int i = 0; i < 5000 && harness.Cooldowns.Count > 0; i++)
            harness.System.Update(0f);
        Assert.Empty(harness.Cooldowns);
    }

    [Fact]
    public void PlayImpactAt_ScalesVelocity_AndDoesNotRaiseTheImpactEvent()
    {
        var bare = new CollisionAudioSystem();
        Assert.False(bare.PlayImpactAt(Vector3.Zero, SurfaceType.Metal, SurfaceType.Wood, 3f).IsValid);

        using var world = new World();
        var system = world.AddSystem<CollisionAudioSystem>();
        var backend = new FakeAudioBackend();
        using var engine = new AudioEngine(world, backend, AcousticQualitySettings.Medium);
        engine.Initialize();
        var config = CollisionSoundConfig.Default;
        config.PitchVariation = 0f;
        config.MaxVolumeVelocity = 8f;
        system.Config = config;
        system.Initialize(new PhysicsWorld(), engine);

        int events = 0;
        system.OnImpactSoundPlayed += (_, _) => events++;
        var voice = system.PlayImpactAt(new Vector3(4, 5, 6), SurfaceType.Wood, SurfaceType.Wood, 8f, 0.25f);
        Assert.True(voice.IsValid);
        Assert.Equal(new Vector3(4, 5, 6), backend.Positions[voice.Id]);
        Assert.Equal(0.25f, backend.Volumes[voice.Id], 1e-4);
        Assert.Equal(1f, backend.Pitches[voice.Id], 1e-4);
        Assert.Contains("wood_wood_impact", backend.LoadedPaths.Single());
        Assert.Equal(0, events);

        var capped = system.PlayImpactAt(Vector3.Zero, SurfaceType.Wood, SurfaceType.Wood, 80f, 1f);
        Assert.Equal(1f, backend.Volumes[capped.Id], 1e-4);
    }

    [Fact(Skip = "BUG: PlayImpactAt plays a one-shot but does not raise OnImpactSoundPlayed. Expected: manual impacts notify the same listeners as physics impacts.")]
    public void PlayImpactAt_RaisesOnImpactSoundPlayed()
    {
        using var world = new World();
        var system = world.AddSystem<CollisionAudioSystem>();
        using var engine = Engine(world);
        system.Initialize(new PhysicsWorld(), engine);
        int events = 0;
        system.OnImpactSoundPlayed += (_, _) => events++;
        system.PlayImpactAt(Vector3.Zero, SurfaceType.Metal, SurfaceType.Metal, 4f);
        Assert.Equal(1, events);
    }

    [Fact]
    public void LibraryFallbacks_AndSurfaceSetter_CoverTheRemainingBranches()
    {
        var library = new ImpactSoundLibrary();
        Assert.Contains("default_impact", library.GetImpactSound(SurfaceType.Carpet, SurfaceType.Paper));
        Assert.Contains("glass_impact", library.GetImpactSound(SurfaceType.Glass, SurfaceType.Sand));
        Assert.Contains("default_slide", library.GetSlideSound(SurfaceType.Grass));

        library.RegisterImpact(SurfaceType.Ice, SurfaceType.Ice);
        Assert.Contains("default_impact", library.GetImpactSound(SurfaceType.Ice, SurfaceType.Ice));
        library.RegisterImpact(SurfaceType.Ice, SurfaceType.Ice, "ice_a", "ice_b");
        Assert.Contains(library.GetImpactSound(SurfaceType.Ice, SurfaceType.Ice), new[]
        {
            "sounds/impacts/ice_a.wav",
            "sounds/impacts/ice_b.wav"
        });

        using var world = new World();
        var entity = world.CreateEntity();
        world.SetCollisionSurface(entity, SurfaceType.Metal);
        Assert.False(world.HasCollisionSound(entity));
    }

    [Fact]
    public void PhysicsStep_PlaysTheMaterialImpactThroughTheRealCollisionCallback()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        var config = CollisionSoundConfig.Default;
        config.PitchVariation = 0f;
        config.EnableSlidingSounds = false;
        config.MinImpactVelocity = 0.2f;
        var collisionAudio = world.AddSystem(new CollisionAudioSystem(config));
        var backend = new FakeAudioBackend();
        using var engine = new AudioEngine(world, backend, AcousticQualitySettings.Low);
        engine.Initialize();
        collisionAudio.Initialize(physics.PhysicsWorld, engine);

        int reported = 0;
        collisionAudio.OnImpactSoundPlayed += (_, voice) =>
        {
            if (voice.IsValid)
                reported++;
        };

        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());
        world.AddCollisionSound(ground, SurfaceType.Stone);

        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 5f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));
        world.AddCollisionSound(ball, SurfaceType.Metal);

        for (int i = 0; i < 200 && backend.PlayedVoices.Count == 0; i++)
            physics.Update(0.016f);

        Assert.NotEmpty(backend.PlayedVoices);
        Assert.Contains(backend.LoadedPaths, path => path.Contains("metal_stone_impact"));
        Assert.Contains(backend.Positions.Values, position => position.Y < 1.5f && position.Y > -1f);
        Assert.True(reported > 0);
        Assert.InRange(backend.Volumes[backend.PlayedVoices[0].Id], 0.01f, 1f);
    }

    private static float ExpectedPitch(float impactSpeed, float combinedMass, float pitchMultiplier)
    {
        _ = impactSpeed;
        float massInfluence = MathF.Log10(combinedMass / 10f + 1f);
        return Math.Clamp((1f - massInfluence * 0.1f) * pitchMultiplier, 0.5f, 2f);
    }

    private static AudioEngine Engine(World world)
    {
        var backend = new FakeAudioBackend();
        var engine = new AudioEngine(world, backend, AcousticQualitySettings.Low);
        engine.Initialize();
        return engine;
    }

    private static int HandlerCount(PhysicsWorld physics, string eventName)
    {
        var handler = EventDelegate(physics, eventName);
        return handler?.GetInvocationList().Length ?? 0;
    }

    private static Delegate? EventDelegate(PhysicsWorld physics, string eventName)
    {
        var field = typeof(PhysicsWorld).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(physics) as Delegate;
    }

    private static void Raise(CollisionAudioSystem system, string methodName, CollisionEvent evt)
    {
        var method = typeof(CollisionAudioSystem).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(system, new object[] { evt });
    }

    private static CollisionEvent Impact(Entity a, Entity b, float speed)
    {
        var bodyA = new RigidBody { EntityId = a.Id, Type = RigidBodyType.Dynamic, Mass = 1f, Position = Vector3.Zero };
        var bodyB = new RigidBody { EntityId = b.Id, Type = RigidBodyType.Dynamic, Mass = 1f, Position = Vector3.UnitY, LinearVelocity = new Vector3(0, -speed, 0) };
        var colliderA = new SphereCollider(0.5f);
        var colliderB = new SphereCollider(0.5f);
        var manifold = new CollisionManifold { BodyA = bodyA, BodyB = bodyB, ColliderA = colliderA, ColliderB = colliderB };
        manifold.Contacts.Add(new CollisionContact { PointOnA = Vector3.Zero, PointOnB = Vector3.Zero, Normal = Vector3.UnitY, Depth = 0.01f });
        return new CollisionEvent { BodyA = bodyA, BodyB = bodyB, ColliderA = colliderA, ColliderB = colliderB, Manifold = manifold };
    }

    private sealed class Harness : IDisposable
    {
        public World World { get; } = new();
        public CollisionAudioSystem System { get; }
        public FakeAudioBackend Backend { get; }
        public AudioEngine Audio { get; }
        public PhysicsWorld Physics { get; } = new();

        public Harness(float maxVolumeVelocity, float minImpact, float cooldown, int maxSounds, bool sliding = false, float minSlide = 1f)
        {
            var config = CollisionSoundConfig.Default;
            config.PitchVariation = 0f;
            config.MaxVolumeVelocity = maxVolumeVelocity;
            config.MinImpactVelocity = minImpact;
            config.Cooldown = cooldown;
            config.MaxSoundsPerFrame = maxSounds;
            config.EnableSlidingSounds = sliding;
            config.MinSlidingVelocity = minSlide;
            System = World.AddSystem(new CollisionAudioSystem(config));
            Backend = new FakeAudioBackend();
            Audio = new AudioEngine(World, Backend, AcousticQualitySettings.Low);
            Audio.Initialize();
            System.Initialize(Physics, Audio);
        }

        public Dictionary<(int, int), float> Cooldowns
        {
            get
            {
                var cooldownField = typeof(CollisionAudioSystem).GetField("_cooldowns", BindingFlags.Instance | BindingFlags.NonPublic);
                return (Dictionary<(int, int), float>)cooldownField!.GetValue(System)!;
            }
        }

        public Entity Entity(SurfaceType surface, float volume, float pitch, float mass = 1f, string? customPath = null, bool enabled = true, float minImpactOverride = 0f)
        {
            var entity = World.CreateEntity();
            World.AddComponent(entity, new CollisionSoundComponent
            {
                SurfaceType = surface,
                VolumeMultiplier = volume,
                PitchMultiplier = pitch,
                CustomSoundPath = customPath,
                Enabled = enabled,
                MinImpactVelocityOverride = minImpactOverride,
                LastImpactTime = float.NegativeInfinity,
                LastCollidedEntityId = -1
            });
            return entity;
        }

        public RigidBody Body(int entityId, float mass, bool dynamic) => new()
        {
            EntityId = entityId,
            Type = dynamic ? RigidBodyType.Dynamic : RigidBodyType.Static,
            Mass = mass
        };

        public void Enter(Entity a, Entity b, float impactSpeed, float massA = 1f, float massB = 1f, Vector3? contact = null)
        {
            RaiseEnter(Pair(a, b, impactSpeed, massA, massB, contact ?? new Vector3(1, 0, 0), slidingSpeed: 0f));
        }

        public void Stay(Entity a, Entity b, float slidingSpeed)
        {
            Raise(System, "HandleCollisionStay", Pair(a, b, impactSpeed: 0.1f, 1f, 1f, Vector3.Zero, slidingSpeed));
        }

        public void RaiseEnter(CollisionEvent evt) => Raise(System, "HandleCollisionEnter", evt);

        public void Dispose()
        {
            Audio.Dispose();
            World.Dispose();
        }

        private static CollisionEvent Pair(Entity a, Entity b, float impactSpeed, float massA, float massB, Vector3 contact, float slidingSpeed)
        {
            var bodyA = new RigidBody
            {
                EntityId = a.Id,
                Type = RigidBodyType.Dynamic,
                Mass = massA,
                Position = contact
            };
            var bodyB = new RigidBody
            {
                EntityId = b.Id,
                Type = RigidBodyType.Dynamic,
                Mass = massB,
                Position = contact,
                LinearVelocity = new Vector3(slidingSpeed, -impactSpeed, 0f)
            };
            var colliderA = new SphereCollider(0.5f);
            var colliderB = new SphereCollider(0.5f);
            var manifold = new CollisionManifold
            {
                BodyA = bodyA,
                BodyB = bodyB,
                ColliderA = colliderA,
                ColliderB = colliderB
            };
            manifold.Contacts.Add(new CollisionContact
            {
                PointOnA = contact,
                PointOnB = contact,
                Normal = Vector3.UnitY,
                Depth = 0.02f
            });
            return new CollisionEvent
            {
                BodyA = bodyA,
                BodyB = bodyB,
                ColliderA = colliderA,
                ColliderB = colliderB,
                Manifold = manifold
            };
        }
    }
}
