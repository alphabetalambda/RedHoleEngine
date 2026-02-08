using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Core.ECS;

// Test component
public struct TestComponent : IComponent
{
    public int Value;
    public string Name;
}

public struct PositionComponent : IComponent
{
    public Vector3 Position;
}

public struct VelocityComponent : IComponent
{
    public Vector3 Velocity;
}

// Test system
public class TestSystem : GameSystem
{
    public int UpdateCount { get; private set; }
    public float TotalTime { get; private set; }
    
    public override void Update(float deltaTime)
    {
        UpdateCount++;
        TotalTime += deltaTime;
    }
}

public class PriorityTestSystem : GameSystem
{
    public int ExecutionOrder { get; set; }
    public static int NextOrder { get; set; }
    
    public override int Priority { get; }
    
    public PriorityTestSystem(int priority)
    {
        Priority = priority;
    }
    
    public override void Update(float deltaTime)
    {
        ExecutionOrder = NextOrder++;
    }
}

public class WorldTests
{
    #region Entity Tests

    [Fact]
    public void CreateEntity_ReturnsValidEntity()
    {
        using var world = new World();
        
        var entity = world.CreateEntity();
        
        Assert.False(entity.IsNull);
        Assert.True(entity.Id > 0);
        Assert.True(entity.Generation > 0);
    }

    [Fact]
    public void CreateEntity_IncreasesEntityCount()
    {
        using var world = new World();
        
        Assert.Equal(0, world.EntityCount);
        
        world.CreateEntity();
        Assert.Equal(1, world.EntityCount);
        
        world.CreateEntity();
        Assert.Equal(2, world.EntityCount);
    }

    [Fact]
    public void CreateEntity_ReturnsUniqueIds()
    {
        using var world = new World();
        
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        var entity3 = world.CreateEntity();
        
        Assert.NotEqual(entity1.Id, entity2.Id);
        Assert.NotEqual(entity2.Id, entity3.Id);
        Assert.NotEqual(entity1.Id, entity3.Id);
    }

    [Fact]
    public void DestroyEntity_DecreasesEntityCount()
    {
        using var world = new World();
        
        var entity = world.CreateEntity();
        Assert.Equal(1, world.EntityCount);
        
        world.DestroyEntity(entity);
        Assert.Equal(0, world.EntityCount);
    }

    [Fact]
    public void IsAlive_ReturnsTrueForLiveEntity()
    {
        using var world = new World();
        
        var entity = world.CreateEntity();
        
        Assert.True(world.IsAlive(entity));
    }

    [Fact]
    public void IsAlive_ReturnsFalseForDestroyedEntity()
    {
        using var world = new World();
        
        var entity = world.CreateEntity();
        world.DestroyEntity(entity);
        
        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void IsAlive_ReturnsFalseForNullEntity()
    {
        using var world = new World();
        
        Assert.False(world.IsAlive(Entity.Null));
    }

    [Fact]
    public void DestroyEntity_RecyclesIds()
    {
        using var world = new World();
        
        var entity1 = world.CreateEntity();
        var id1 = entity1.Id;
        
        world.DestroyEntity(entity1);
        
        var entity2 = world.CreateEntity();
        
        // ID should be recycled
        Assert.Equal(id1, entity2.Id);
        // But generation should be different
        Assert.NotEqual(entity1.Generation, entity2.Generation);
    }

    [Fact]
    public void DestroyEntity_InvalidatesOldReference()
    {
        using var world = new World();
        
        var entity1 = world.CreateEntity();
        world.DestroyEntity(entity1);
        
        var entity2 = world.CreateEntity();
        
        // Old reference should be invalid even if ID is recycled
        Assert.False(world.IsAlive(entity1));
        Assert.True(world.IsAlive(entity2));
    }

    #endregion

    #region Component Tests

    [Fact]
    public void AddComponent_CanBeRetrieved()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        
        world.AddComponent(entity, new TestComponent { Value = 42, Name = "Test" });
        
        ref var component = ref world.GetComponent<TestComponent>(entity);
        
        Assert.Equal(42, component.Value);
        Assert.Equal("Test", component.Name);
    }

    [Fact]
    public void AddComponent_ReturnsReference()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        
        ref var component = ref world.AddComponent(entity, new TestComponent { Value = 1 });
        component.Value = 100;
        
        ref var retrieved = ref world.GetComponent<TestComponent>(entity);
        Assert.Equal(100, retrieved.Value);
    }

    [Fact]
    public void HasComponent_ReturnsTrueWhenPresent()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        
        world.AddComponent(entity, new TestComponent());
        
        Assert.True(world.HasComponent<TestComponent>(entity));
    }

    [Fact]
    public void HasComponent_ReturnsFalseWhenAbsent()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        
        Assert.False(world.HasComponent<TestComponent>(entity));
    }

    [Fact]
    public void RemoveComponent_RemovesComponent()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        
        world.AddComponent(entity, new TestComponent());
        Assert.True(world.HasComponent<TestComponent>(entity));
        
        world.RemoveComponent<TestComponent>(entity);
        Assert.False(world.HasComponent<TestComponent>(entity));
    }

    [Fact]
    public void TryGetComponent_ReturnsTrueAndValue()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TestComponent { Value = 42 });
        
        bool found = world.TryGetComponent<TestComponent>(entity, out var component);
        
        Assert.True(found);
        Assert.Equal(42, component.Value);
    }

    [Fact]
    public void TryGetComponent_ReturnsFalseWhenMissing()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        
        bool found = world.TryGetComponent<TestComponent>(entity, out _);
        
        Assert.False(found);
    }

    [Fact]
    public void DestroyEntity_RemovesAllComponents()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        
        world.AddComponent(entity, new TestComponent());
        world.AddComponent(entity, new PositionComponent());
        
        world.DestroyEntity(entity);
        
        // Create new entity with same recycled ID
        var newEntity = world.CreateEntity();
        
        // Should not have components from old entity
        Assert.False(world.HasComponent<TestComponent>(newEntity));
        Assert.False(world.HasComponent<PositionComponent>(newEntity));
    }

    [Fact]
    public void MultipleComponents_OnSameEntity()
    {
        using var world = new World();
        var entity = world.CreateEntity();
        
        world.AddComponent(entity, new TestComponent { Value = 1 });
        world.AddComponent(entity, new PositionComponent { Position = new Vector3(10, 20, 30) });
        world.AddComponent(entity, new VelocityComponent { Velocity = new Vector3(1, 2, 3) });
        
        Assert.True(world.HasComponent<TestComponent>(entity));
        Assert.True(world.HasComponent<PositionComponent>(entity));
        Assert.True(world.HasComponent<VelocityComponent>(entity));
        
        Assert.Equal(1, world.GetComponent<TestComponent>(entity).Value);
        Assert.Equal(new Vector3(10, 20, 30), world.GetComponent<PositionComponent>(entity).Position);
        Assert.Equal(new Vector3(1, 2, 3), world.GetComponent<VelocityComponent>(entity).Velocity);
    }

    #endregion

    #region Query Tests

    [Fact]
    public void Query_ReturnsEntitiesWithComponent()
    {
        using var world = new World();
        
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        var entity3 = world.CreateEntity();
        
        world.AddComponent(entity1, new TestComponent());
        world.AddComponent(entity3, new TestComponent());
        // entity2 has no TestComponent
        
        var results = world.Query<TestComponent>().ToList();
        
        Assert.Equal(2, results.Count);
        Assert.Contains(entity1, results);
        Assert.Contains(entity3, results);
        Assert.DoesNotContain(entity2, results);
    }

    [Fact]
    public void Query_TwoComponents_ReturnsEntitiesWithBoth()
    {
        using var world = new World();
        
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        var entity3 = world.CreateEntity();
        
        world.AddComponent(entity1, new PositionComponent());
        world.AddComponent(entity1, new VelocityComponent());
        
        world.AddComponent(entity2, new PositionComponent());
        // entity2 has no VelocityComponent
        
        world.AddComponent(entity3, new VelocityComponent());
        // entity3 has no PositionComponent
        
        var results = world.Query<PositionComponent, VelocityComponent>().ToList();
        
        Assert.Single(results);
        Assert.Contains(entity1, results);
    }

    [Fact]
    public void Query_ThreeComponents_ReturnsEntitiesWithAll()
    {
        using var world = new World();
        
        var entity1 = world.CreateEntity();
        var entity2 = world.CreateEntity();
        
        world.AddComponent(entity1, new TestComponent());
        world.AddComponent(entity1, new PositionComponent());
        world.AddComponent(entity1, new VelocityComponent());
        
        world.AddComponent(entity2, new TestComponent());
        world.AddComponent(entity2, new PositionComponent());
        // entity2 missing VelocityComponent
        
        var results = world.Query<TestComponent, PositionComponent, VelocityComponent>().ToList();
        
        Assert.Single(results);
        Assert.Contains(entity1, results);
    }

    [Fact]
    public void Query_EmptyResults_ReturnsEmptyEnumerable()
    {
        using var world = new World();
        
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TestComponent());
        
        var results = world.Query<PositionComponent>().ToList();
        
        Assert.Empty(results);
    }

    #endregion

    #region System Tests

    [Fact]
    public void AddSystem_RegistersSystem()
    {
        using var world = new World();
        
        var system = world.AddSystem<TestSystem>();
        
        Assert.NotNull(system);
        Assert.Contains(system, world.Systems);
    }

    [Fact]
    public void GetSystem_ReturnsRegisteredSystem()
    {
        using var world = new World();
        
        world.AddSystem<TestSystem>();
        
        var retrieved = world.GetSystem<TestSystem>();
        
        Assert.NotNull(retrieved);
    }

    [Fact]
    public void GetSystem_ReturnsNullForUnregistered()
    {
        using var world = new World();
        
        var retrieved = world.GetSystem<TestSystem>();
        
        Assert.Null(retrieved);
    }

    [Fact]
    public void Update_CallsSystemUpdate()
    {
        using var world = new World();
        var system = world.AddSystem<TestSystem>();
        
        world.Update(0.016f);
        
        Assert.Equal(1, system.UpdateCount);
        Assert.Equal(0.016f, system.TotalTime, 0.001f);
    }

    [Fact]
    public void Update_CallsMultipleSystems()
    {
        using var world = new World();
        var system1 = world.AddSystem<TestSystem>();
        var system2 = world.AddSystem<TestSystem>();
        
        world.Update(0.016f);
        
        Assert.Equal(1, system1.UpdateCount);
        Assert.Equal(1, system2.UpdateCount);
    }

    [Fact]
    public void Update_RespectsSystemPriority()
    {
        using var world = new World();
        PriorityTestSystem.NextOrder = 0;
        
        var lowPriority = world.AddSystem(new PriorityTestSystem(100));
        var highPriority = world.AddSystem(new PriorityTestSystem(-100));
        var mediumPriority = world.AddSystem(new PriorityTestSystem(0));
        
        world.Update(0.016f);
        
        // Lower priority number = runs first
        Assert.Equal(0, highPriority.ExecutionOrder);
        Assert.Equal(1, mediumPriority.ExecutionOrder);
        Assert.Equal(2, lowPriority.ExecutionOrder);
    }

    [Fact]
    public void Update_SkipsDisabledSystems()
    {
        using var world = new World();
        var system = world.AddSystem<TestSystem>();
        system.Enabled = false;
        
        world.Update(0.016f);
        
        Assert.Equal(0, system.UpdateCount);
    }

    [Fact]
    public void RemoveSystem_RemovesFromWorld()
    {
        using var world = new World();
        world.AddSystem<TestSystem>();
        
        world.RemoveSystem<TestSystem>();
        
        Assert.Null(world.GetSystem<TestSystem>());
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ClearsAllData()
    {
        var world = new World();
        
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TestComponent());
        world.AddSystem<TestSystem>();
        
        world.Dispose();
        
        Assert.Empty(world.Systems);
        // Can't easily check pools are cleared, but they should be
    }

    #endregion
}
