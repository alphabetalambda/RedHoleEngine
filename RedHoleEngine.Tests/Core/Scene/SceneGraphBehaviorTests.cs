using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;

namespace RedHoleEngine.Tests.Core.Scene;

public class SceneGraphBehaviorTests
{
    [Fact]
    public void CreateNode_Parent_RemovesChildFromRoots_AndFindWalksHierarchy()
    {
        var scene = new RedHoleEngine.Core.Scene.Scene("Level");
        var root = scene.CreateNode("Root");
        root.Tag = "world";
        var child = scene.CreateNode(root, "Child");
        child.Tag = "pickup";
        var grand = scene.CreateNode(child, "Grand");

        Assert.Equal("Level", scene.Name);
        Assert.Single(scene.RootNodes);
        Assert.Equal(root, scene.RootNodes[0]);
        Assert.Equal(root, child.Parent);
        Assert.Equal(root, grand.GetRoot());
        Assert.True(root.IsAncestorOf(grand));
        Assert.False(grand.IsAncestorOf(root));
        Assert.Equal(child, scene.FindNode("Child"));
        Assert.Equal(grand, root.FindDescendant("Grand"));
        Assert.Equal(child, root.FindChild("Child"));
        Assert.Null(root.FindChild("Grand"));
        Assert.Null(scene.FindNode("missing"));
        Assert.Contains(child, scene.FindByTag("pickup"));
        Assert.Contains(child, root.FindByTag("pickup"));
        Assert.Equal(new[] { "Root", "Child", "Grand" }, scene.GetAllNodes().Select(n => n.Name).ToArray());
        Assert.Contains("Nodes=3", scene.ToString());

        grand.Active = false;
        Assert.Equal(new[] { "Root", "Child" }, scene.GetActiveNodes().Select(n => n.Name).ToArray());

        child.Active = false;
        Assert.Equal(new[] { "Root" }, scene.GetActiveNodes().Select(n => n.Name).ToArray());
    }

    [Fact]
    public void ReparentToNull_ReturnsNodeToRoots_AndCircularParentThrows()
    {
        var scene = new RedHoleEngine.Core.Scene.Scene(new World(), "W");
        var a = scene.CreateNode("A");
        var b = scene.CreateNode(a, "B");

        b.SetParent(null);
        Assert.Null(b.Parent);
        Assert.Contains(b, scene.RootNodes);
        Assert.DoesNotContain(b, a.Children);

        b.SetParent(a);
        var ex = Assert.Throws<InvalidOperationException>(() => a.SetParent(b));
        Assert.Contains("circular", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Setting the same parent is a no-op.
        b.SetParent(a);
        Assert.Equal(a, b.Parent);
        Assert.Single(a.Children);
    }

    [Fact]
    public void AddChild_RemoveChild_AndComponentBridge()
    {
        using var world = new World();
        var scene = new RedHoleEngine.Core.Scene.Scene(world);
        var parent = scene.CreateNode("P");
        var external = new SceneNode(world, "Ext");
        scene.RegisterNode(external);
        parent.AddChild(external);

        Assert.Equal(parent, external.Parent);
        Assert.DoesNotContain(external, scene.RootNodes);

        external.AddComponent(new TransformComponent(new Vector3(3, 0, 0)));
        Assert.True(external.HasComponent<TransformComponent>());
        Assert.Equal(3f, external.GetComponent<TransformComponent>().Position.X);
        Assert.True(external.TryGetComponent<TransformComponent>(out var copy));
        Assert.Equal(3f, copy.Position.X);
        Assert.Contains(external, parent.FindWithComponent<TransformComponent>());

        external.RemoveComponent<TransformComponent>();
        Assert.False(external.HasComponent<TransformComponent>());

        parent.RemoveChild(external);
        Assert.Null(external.Parent);
        Assert.Contains(external, scene.RootNodes);
        parent.RemoveChild(external);
    }

    [Fact]
    public void DestroyNode_RemovesDescendants_AndClearDisposesWorldOnSceneDispose()
    {
        var scene = new RedHoleEngine.Core.Scene.Scene("Temp");
        var root = scene.CreateNode("R");
        var child = scene.CreateNode(root, "C");
        var childEntity = child.Entity;

        scene.DestroyNode(root);
        Assert.Empty(scene.RootNodes);
        Assert.Null(scene.GetNode(childEntity));
        Assert.Empty(scene.GetAllNodes());

        scene.CreateNode("Again");
        scene.Clear();
        Assert.Empty(scene.RootNodes);

        scene.Dispose();
    }

    [Fact]
    public void WorldPositionStays_WhenParented()
    {
        var scene = new RedHoleEngine.Core.Scene.Scene();
        var parent = scene.CreateNode("P");
        parent.Transform.Position = new Vector3(10, 0, 0);
        var child = scene.CreateNode("C");
        child.Transform.Position = new Vector3(12, 0, 0);

        child.SetParent(parent, worldPositionStays: true);
        Assert.Equal(12f, child.Transform.Position.X, 0.01f);

        child.SetParent(null, worldPositionStays: false);
    }

    [Fact]
    public void SceneUpdate_TicksRegisteredSystems()
    {
        var scene = new RedHoleEngine.Core.Scene.Scene();
        var system = scene.World.AddSystem<CountingSystem>();
        scene.Update(0.016f);
        scene.FixedUpdate(0.016f);
        Assert.Equal(1, system.Updates);
        Assert.Equal(1, system.FixedUpdates);
    }

    private sealed class CountingSystem : GameSystem
    {
        public int Updates;
        public int FixedUpdates;
        public override void Update(float deltaTime) => Updates++;
        public override void FixedUpdate(float fixedDeltaTime) => FixedUpdates++;
    }
}
