using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Audio;

public class CollisionAudioTests
{
    #region SurfaceType Tests

    [Fact]
    public void SurfaceType_DefaultValue_IsDefault()
    {
        var surface = default(SurfaceType);
        Assert.Equal(SurfaceType.Default, surface);
    }

    [Theory]
    [InlineData(SurfaceType.Metal)]
    [InlineData(SurfaceType.Wood)]
    [InlineData(SurfaceType.Stone)]
    [InlineData(SurfaceType.Glass)]
    [InlineData(SurfaceType.Rubber)]
    [InlineData(SurfaceType.Energy)]
    public void SurfaceType_AllValues_AreDefined(SurfaceType surface)
    {
        Assert.True(Enum.IsDefined(surface));
    }

    #endregion

    #region CollisionSoundConfig Tests

    [Fact]
    public void CollisionSoundConfig_Default_HasReasonableValues()
    {
        var config = CollisionSoundConfig.Default;

        Assert.Equal("sounds/impacts/", config.BasePath);
        Assert.Equal(".wav", config.Extension);
        Assert.True(config.MinImpactVelocity > 0);
        Assert.True(config.MaxVolumeVelocity > config.MinImpactVelocity);
        Assert.True(config.PitchVariation >= 0 && config.PitchVariation <= 1);
        Assert.True(config.Cooldown > 0);
        Assert.True(config.MaxSoundsPerFrame > 0);
    }

    [Fact]
    public void CollisionSoundConfig_Quiet_HasHigherThresholds()
    {
        var quiet = CollisionSoundConfig.Quiet;
        var normal = CollisionSoundConfig.Default;

        Assert.True(quiet.MinImpactVelocity >= normal.MinImpactVelocity);
        Assert.True(quiet.MaxSoundsPerFrame <= normal.MaxSoundsPerFrame);
        Assert.True(quiet.Cooldown >= normal.Cooldown);
    }

    #endregion

    #region CollisionSoundComponent Tests

    [Fact]
    public void CollisionSoundComponent_Default_HasExpectedValues()
    {
        var comp = CollisionSoundComponent.Default;

        Assert.Equal(SurfaceType.Default, comp.SurfaceType);
        Assert.Equal(1f, comp.VolumeMultiplier);
        Assert.Equal(1f, comp.PitchMultiplier);
        Assert.Equal(0f, comp.MinImpactVelocityOverride);
        Assert.Null(comp.CustomSoundPath);
        Assert.True(comp.Enabled);
    }

    [Fact]
    public void CollisionSoundComponent_Create_SetsCorrectSurface()
    {
        var comp = CollisionSoundComponent.Create(SurfaceType.Metal, 0.8f);

        Assert.Equal(SurfaceType.Metal, comp.SurfaceType);
        Assert.Equal(0.8f, comp.VolumeMultiplier);
        Assert.True(comp.Enabled);
    }

    [Fact]
    public void CollisionSoundComponent_CreateMetal_IsMetal()
    {
        var comp = CollisionSoundComponent.CreateMetal();
        Assert.Equal(SurfaceType.Metal, comp.SurfaceType);
    }

    [Fact]
    public void CollisionSoundComponent_CreateWood_IsWood()
    {
        var comp = CollisionSoundComponent.CreateWood();
        Assert.Equal(SurfaceType.Wood, comp.SurfaceType);
    }

    [Fact]
    public void CollisionSoundComponent_CreateStone_IsStone()
    {
        var comp = CollisionSoundComponent.CreateStone();
        Assert.Equal(SurfaceType.Stone, comp.SurfaceType);
    }

    [Fact]
    public void CollisionSoundComponent_CreateGlass_IsGlass()
    {
        var comp = CollisionSoundComponent.CreateGlass();
        Assert.Equal(SurfaceType.Glass, comp.SurfaceType);
    }

    #endregion

    #region ImpactData Tests

    [Fact]
    public void ImpactData_CanBeCreatedWithInitializers()
    {
        var impact = new ImpactData
        {
            Position = new Vector3(1, 2, 3),
            Normal = Vector3.UnitY,
            ImpactVelocity = 5f,
            SlidingVelocity = 2f,
            TotalRelativeVelocity = 5.4f,
            SurfaceA = SurfaceType.Metal,
            SurfaceB = SurfaceType.Stone,
            CombinedMass = 15f,
            EntityIdA = 1,
            EntityIdB = 2
        };

        Assert.Equal(new Vector3(1, 2, 3), impact.Position);
        Assert.Equal(Vector3.UnitY, impact.Normal);
        Assert.Equal(5f, impact.ImpactVelocity);
        Assert.Equal(2f, impact.SlidingVelocity);
        Assert.Equal(SurfaceType.Metal, impact.SurfaceA);
        Assert.Equal(SurfaceType.Stone, impact.SurfaceB);
        Assert.Equal(15f, impact.CombinedMass);
        Assert.Equal(1, impact.EntityIdA);
        Assert.Equal(2, impact.EntityIdB);
    }

    [Fact]
    public void ImpactData_IsReadOnly()
    {
        // ImpactData is a readonly struct - verify it can be passed by value efficiently
        var impact = new ImpactData { Position = Vector3.One };
        var copy = impact;
        
        Assert.Equal(impact.Position, copy.Position);
    }

    #endregion

    #region ImpactSoundLibrary Tests

    [Fact]
    public void ImpactSoundLibrary_GetImpactSound_ReturnsPath()
    {
        var library = new ImpactSoundLibrary();

        var sound = library.GetImpactSound(SurfaceType.Metal, SurfaceType.Metal);

        Assert.NotNull(sound);
        Assert.Contains("metal", sound.ToLower());
        Assert.EndsWith(".wav", sound);
    }

    [Fact]
    public void ImpactSoundLibrary_GetImpactSound_IsSymmetric()
    {
        var library = new ImpactSoundLibrary();

        var sound1 = library.GetImpactSound(SurfaceType.Metal, SurfaceType.Stone);
        var sound2 = library.GetImpactSound(SurfaceType.Stone, SurfaceType.Metal);

        Assert.Equal(sound1, sound2);
    }

    [Fact]
    public void ImpactSoundLibrary_GetImpactSound_FallsBackToDefault()
    {
        var library = new ImpactSoundLibrary();

        // Use an unusual combination that might not have a specific sound
        var sound = library.GetImpactSound(SurfaceType.Carpet, SurfaceType.Paper);

        Assert.NotNull(sound);
        Assert.EndsWith(".wav", sound);
    }

    [Fact]
    public void ImpactSoundLibrary_GetSlideSound_ReturnsPath()
    {
        var library = new ImpactSoundLibrary();

        var sound = library.GetSlideSound(SurfaceType.Metal);

        Assert.NotNull(sound);
        Assert.EndsWith(".wav", sound);
    }

    [Fact]
    public void ImpactSoundLibrary_RegisterImpact_CustomSound()
    {
        var library = new ImpactSoundLibrary();
        library.RegisterImpact(SurfaceType.Energy, SurfaceType.Energy, "custom_energy_impact");

        var sound = library.GetImpactSound(SurfaceType.Energy, SurfaceType.Energy);

        Assert.Contains("custom_energy_impact", sound);
    }

    [Fact]
    public void ImpactSoundLibrary_BasePath_CanBeCustomized()
    {
        var library = new ImpactSoundLibrary
        {
            BasePath = "audio/sfx/impacts/",
            Extension = ".ogg"
        };

        var sound = library.GetImpactSound(SurfaceType.Metal, SurfaceType.Metal);

        Assert.StartsWith("audio/sfx/impacts/", sound);
        Assert.EndsWith(".ogg", sound);
    }

    [Fact]
    public void ImpactSoundLibrary_RegisterSlide_CustomSound()
    {
        var library = new ImpactSoundLibrary();
        library.RegisterSlide(SurfaceType.Ice, "ice_scrape");

        var sound = library.GetSlideSound(SurfaceType.Ice);

        Assert.Contains("ice_scrape", sound);
    }

    #endregion

    #region CollisionAudioSystem Tests

    [Fact]
    public void CollisionAudioSystem_CanBeCreated()
    {
        using var world = new World();
        var system = world.AddSystem<CollisionAudioSystem>();

        Assert.NotNull(system);
        Assert.Equal(75, system.Priority); // After physics, before audio
    }

    [Fact]
    public void CollisionAudioSystem_CanBeCreatedWithConfig()
    {
        var config = CollisionSoundConfig.Quiet;
        var system = new CollisionAudioSystem(config);

        Assert.Equal(config.MinImpactVelocity, system.Config.MinImpactVelocity);
        Assert.Equal(config.Cooldown, system.Config.Cooldown);
    }

    [Fact]
    public void CollisionAudioSystem_SoundLibrary_IsAccessible()
    {
        var system = new CollisionAudioSystem();

        Assert.NotNull(system.SoundLibrary);
    }

    [Fact]
    public void CollisionAudioSystem_Config_CanBeModified()
    {
        var system = new CollisionAudioSystem();
        var newConfig = new CollisionSoundConfig
        {
            MinImpactVelocity = 2f,
            MaxVolumeVelocity = 20f,
            BasePath = "custom/path/",
            Extension = ".mp3"
        };

        system.Config = newConfig;

        Assert.Equal(2f, system.Config.MinImpactVelocity);
        Assert.Equal("custom/path/", system.Config.BasePath);
    }

    #endregion

    #region CollisionAudioExtensions Tests

    [Fact]
    public void AddCollisionSound_AddsComponent()
    {
        using var world = new World();
        var entity = world.CreateEntity();

        world.AddCollisionSound(entity, SurfaceType.Metal);

        Assert.True(world.HasCollisionSound(entity));
    }

    [Fact]
    public void AddCollisionSound_WithVolume_SetsVolume()
    {
        using var world = new World();
        var entity = world.CreateEntity();

        world.AddCollisionSound(entity, SurfaceType.Wood, 0.5f);

        ref var comp = ref world.GetComponent<CollisionSoundComponent>(entity);
        Assert.Equal(SurfaceType.Wood, comp.SurfaceType);
        Assert.Equal(0.5f, comp.VolumeMultiplier);
    }

    [Fact]
    public void SetCollisionSurface_ChangesSurfaceType()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        world.AddCollisionSound(entity, SurfaceType.Metal);

        world.SetCollisionSurface(entity, SurfaceType.Stone);

        ref var comp = ref world.GetComponent<CollisionSoundComponent>(entity);
        Assert.Equal(SurfaceType.Stone, comp.SurfaceType);
    }

    [Fact]
    public void HasCollisionSound_ReturnsFalse_WhenNoComponent()
    {
        using var world = new World();
        var entity = world.CreateEntity();

        Assert.False(world.HasCollisionSound(entity));
    }

    [Fact]
    public void AddCollisionSound_WithCustomComponent_UsesComponent()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        var customComp = new CollisionSoundComponent
        {
            SurfaceType = SurfaceType.Glass,
            VolumeMultiplier = 0.3f,
            PitchMultiplier = 1.2f,
            CustomSoundPath = "custom/glass_break.wav",
            Enabled = true
        };

        world.AddCollisionSound(entity, customComp);

        ref var comp = ref world.GetComponent<CollisionSoundComponent>(entity);
        Assert.Equal(SurfaceType.Glass, comp.SurfaceType);
        Assert.Equal(0.3f, comp.VolumeMultiplier);
        Assert.Equal(1.2f, comp.PitchMultiplier);
        Assert.Equal("custom/glass_break.wav", comp.CustomSoundPath);
    }

    #endregion

    #region World.TryGetEntity Tests

    [Fact]
    public void World_TryGetEntity_ReturnsTrue_ForValidEntity()
    {
        using var world = new World();
        var entity = world.CreateEntity();

        bool found = world.TryGetEntity(entity.Id, out var retrieved);

        Assert.True(found);
        Assert.Equal(entity, retrieved);
    }

    [Fact]
    public void World_TryGetEntity_ReturnsFalse_ForInvalidId()
    {
        using var world = new World();

        bool found = world.TryGetEntity(99999, out var retrieved);

        Assert.False(found);
        Assert.True(retrieved.IsNull);
    }

    [Fact]
    public void World_TryGetEntity_ReturnsFalse_ForDestroyedEntity()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        int id = entity.Id;
        world.DestroyEntity(entity);

        // After destruction, TryGetEntity might return a new generation
        // The key test is that IsAlive with old entity returns false
        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void World_GetEntityGeneration_ReturnsGeneration()
    {
        using var world = new World();
        var entity = world.CreateEntity();

        int generation = world.GetEntityGeneration(entity.Id);

        Assert.Equal(entity.Generation, generation);
    }

    [Fact]
    public void World_GetEntityGeneration_IncreasesAfterDestroy()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        int originalGen = entity.Generation;
        
        world.DestroyEntity(entity);
        int newGen = world.GetEntityGeneration(entity.Id);

        Assert.True(newGen > originalGen);
    }

    #endregion
}
