using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Core.ECS;

public class GameSystemAndEntityTests
{
    private struct Alpha : IComponent
    {
        public int Value;
    }

    private struct Beta : IComponent
    {
        public int Value;
    }

    private struct Gamma : IComponent
    {
        public int Value;
    }

    private sealed class InitSystem : GameSystem
    {
        public bool Initialized;
        public bool Destroyed;
        public int FixedUpdates;
        public World? SeenWorld;

        public World ExposedWorld => World;

        protected override void OnInitialize()
        {
            Initialized = true;
            SeenWorld = World;
        }

        public override void Update(float deltaTime) { }

        public override void FixedUpdate(float fixedDeltaTime) => FixedUpdates++;

        public override void OnDestroy() => Destroyed = true;
    }

    private sealed class AlphaSystem : ComponentSystem<Alpha>
    {
        public Entity Seen;
        public float SeenDt;

        protected override void Process(Entity entity, ref Alpha component, float deltaTime)
        {
            Seen = entity;
            SeenDt = deltaTime;
            component.Value += 3;
        }
    }

    private sealed class PairSystem : ComponentSystem<Alpha, Beta>
    {
        public int Processed;

        protected override void Process(Entity entity, ref Alpha alpha, ref Beta beta, float deltaTime)
        {
            Processed++;
            alpha.Value += 1;
            beta.Value += 5;
        }
    }

    private sealed class TripleSystem : ComponentSystem<Alpha, Beta, Gamma>
    {
        public int Processed;

        protected override void Process(Entity entity, ref Alpha alpha, ref Beta beta, ref Gamma gamma, float deltaTime)
        {
            Processed++;
            alpha.Value += 1;
            beta.Value += 1;
            gamma.Value += 1;
        }
    }

    [Fact]
    public void AddSystem_InitializesAndRemoveCallsOnDestroy()
    {
        using var world = new World();
        var system = world.AddSystem(new InitSystem());

        Assert.True(system.Initialized);
        Assert.Same(world, system.SeenWorld);
        Assert.Same(world, system.ExposedWorld);
        Assert.True(system.Enabled);
        Assert.Equal(0, system.Priority);

        world.FixedUpdate(0.02f);
        Assert.Equal(1, system.FixedUpdates);

        system.Enabled = false;
        world.FixedUpdate(0.02f);
        Assert.Equal(1, system.FixedUpdates);

        world.RemoveSystem<InitSystem>();
        Assert.True(system.Destroyed);
        Assert.Null(world.GetSystem<InitSystem>());
    }

    [Fact]
    public void ComponentSystem_Single_MutatesComponentAndPassesZeroGeneration()
    {
        using var world = new World();
        var system = world.AddSystem(new AlphaSystem());
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Alpha { Value = 4 });
        var ignored = world.CreateEntity();
        world.AddComponent(ignored, new Beta { Value = 1 });

        world.Update(0.25f);

        Assert.Equal(7, world.GetComponent<Alpha>(entity).Value);
        Assert.Equal(entity.Id, system.Seen.Id);
        Assert.Equal(0, system.Seen.Generation);
        Assert.Equal(0.25f, system.SeenDt);
        Assert.False(world.IsAlive(system.Seen));
    }

    [Fact(Skip = "BUG: expected ComponentSystem to pass the live entity generation, actual Process receives Entity(id, 0)")]
    public void ComponentSystem_Single_PassesLiveGeneration()
    {
        using var world = new World();
        var system = world.AddSystem(new AlphaSystem());
        var entity = world.CreateEntity();
        world.AddComponent(entity, new Alpha { Value = 1 });

        world.Update(0.1f);

        Assert.Equal(entity.Generation, system.Seen.Generation);
        Assert.True(world.IsAlive(system.Seen));
    }

    [Fact]
    public void ComponentSystem_Pair_PersistsFirstComponentOnly()
    {
        using var world = new World();
        var system = world.AddSystem(new PairSystem());
        var both = world.CreateEntity();
        world.AddComponent(both, new Alpha { Value = 1 });
        world.AddComponent(both, new Beta { Value = 2 });
        var onlyAlpha = world.CreateEntity();
        world.AddComponent(onlyAlpha, new Alpha { Value = 8 });

        world.Update(0.1f);

        Assert.Equal(1, system.Processed);
        Assert.Equal(2, world.GetComponent<Alpha>(both).Value);
        Assert.Equal(2, world.GetComponent<Beta>(both).Value);
        Assert.Equal(8, world.GetComponent<Alpha>(onlyAlpha).Value);
    }

    [Fact(Skip = "BUG: expected ComponentSystem<T1,T2> to write T2 back to the pool, actual TryGet copies the struct so T2 mutations are discarded")]
    public void ComponentSystem_Pair_PersistsSecondComponent()
    {
        using var world = new World();
        world.AddSystem(new PairSystem());
        var both = world.CreateEntity();
        world.AddComponent(both, new Alpha { Value = 1 });
        world.AddComponent(both, new Beta { Value = 2 });

        world.Update(0.1f);

        Assert.Equal(7, world.GetComponent<Beta>(both).Value);
    }

    [Fact]
    public void ComponentSystem_Triple_RequiresAllThreeAndDropsLaterCopies()
    {
        using var world = new World();
        var system = world.AddSystem(new TripleSystem());
        var full = world.CreateEntity();
        world.AddComponent(full, new Alpha { Value = 1 });
        world.AddComponent(full, new Beta { Value = 2 });
        world.AddComponent(full, new Gamma { Value = 3 });
        var partial = world.CreateEntity();
        world.AddComponent(partial, new Alpha { Value = 4 });
        world.AddComponent(partial, new Beta { Value = 5 });

        world.Update(0.1f);

        Assert.Equal(1, system.Processed);
        Assert.Equal(2, world.GetComponent<Alpha>(full).Value);
        Assert.Equal(2, world.GetComponent<Beta>(full).Value);
        Assert.Equal(3, world.GetComponent<Gamma>(full).Value);
        Assert.Equal(4, world.GetComponent<Alpha>(partial).Value);
    }

    [Fact(Skip = "BUG: expected ComponentSystem<T1,T2,T3> to persist T2 and T3 mutations, actual only the T1 pool reference is updated")]
    public void ComponentSystem_Triple_PersistsEveryComponent()
    {
        using var world = new World();
        world.AddSystem(new TripleSystem());
        var full = world.CreateEntity();
        world.AddComponent(full, new Alpha { Value = 1 });
        world.AddComponent(full, new Beta { Value = 2 });
        world.AddComponent(full, new Gamma { Value = 3 });

        world.Update(0.1f);

        Assert.Equal(3, world.GetComponent<Beta>(full).Value);
        Assert.Equal(4, world.GetComponent<Gamma>(full).Value);
    }

    [Fact]
    public void Entity_EqualityHashAndNull()
    {
        var a = new Entity(4, 2);
        var same = new Entity(4, 2);
        var otherGeneration = new Entity(4, 3);
        var otherId = new Entity(5, 2);
        var idOnly = new Entity(5, 0);
        var generationOnly = new Entity(0, 5);

        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals("entity"));
        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a != otherGeneration);
        Assert.True(a != otherId);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.Equal("Entity(4:2)", a.ToString());
        Assert.True(Entity.Null.IsNull);
        Assert.False(a.IsNull);
        Assert.False(idOnly.IsNull);
        Assert.False(generationOnly.IsNull);
        Assert.Equal(Entity.Null, new Entity(0, 0));

        var map = new Dictionary<Entity, string> { [a] = "alive" };
        Assert.Equal("alive", map[same]);
        Assert.False(map.ContainsKey(otherGeneration));
    }
}
