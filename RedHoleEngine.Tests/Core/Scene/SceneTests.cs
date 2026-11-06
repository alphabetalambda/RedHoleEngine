using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;

namespace RedHoleEngine.Tests.CoreScene;

public class SceneTests
{
    private struct Marker : IComponent
    {
        public int Value;
    }

    private sealed class ProbeSystem : GameSystem
    {
        public int Updates;
        public int FixedUpdates;
        public override void Update(float deltaTime) => Updates++;
        public override void FixedUpdate(float fixedDeltaTime) => FixedUpdates++;
    }

    [Fact]
    public void Constructor_Default_CreatesUntitledSceneAndOwnWorld()
    {
        var scene = new Scene();

        Assert.Equal("Untitled", scene.Name);
        Assert.Empty(scene.RootNodes);
        Assert.Equal(0, scene.World.EntityCount);
        Assert.Equal("Scene(Untitled, Nodes=0)", scene.ToString());
    }

    [Fact]
    public void Constructor_UsesProvidedWorldAndName()
    {
        using var world = new World();
        var existing = world.CreateEntity();
        var scene = new Scene(world, "Station");

        Assert.Equal("Station", scene.Name);
        Assert.Same(world, scene.World);
        Assert.True(scene.World.IsAlive(existing));
    }

    [Fact]
    public void CreateNode_RegistersRootAndFindsIt()
    {
        var scene = new Scene("Level");
        var node = scene.CreateNode("Door");

        Assert.Equal("Door", node.Name);
        Assert.True(node.Active);
        Assert.Null(node.Parent);
        Assert.Contains(node, scene.RootNodes);
        Assert.Same(node, scene.GetNode(node.Entity));
        Assert.Same(node, scene.FindNode("Door"));
        Assert.Null(scene.FindNode("Missing"));
        Assert.Null(scene.GetNode(new Entity(99, 1)));
        Assert.Equal(new[] { node }, scene.GetAllNodes().ToArray());
        Assert.Equal("Scene(Level, Nodes=1)", scene.ToString());
    }

    [Fact]
    public void CreateNode_WithParent_KeepsChildInRootNodes()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = scene.CreateNode(parent, "Child");

        Assert.Same(parent, child.Parent);
        Assert.Contains(child, parent.Children);
        Assert.DoesNotContain(child, scene.RootNodes);
        Assert.Equal(1, scene.GetAllNodes().Count(node => node == child));
    }

    [Fact]
    public void FindByTag_ParentedCreateNode_YieldsChildTwice()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = scene.CreateNode(parent, "Child");
        parent.Tag = "enemy";
        child.Tag = "enemy";

        Assert.Equal(new[] { parent, child }, scene.FindByTag("enemy").ToArray());
    }

    [Fact]
    public void CreateNode_WithParent_RemovesChildFromRootNodes()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = scene.CreateNode(parent, "Child");
        parent.Tag = "enemy";
        child.Tag = "enemy";

        Assert.DoesNotContain(child, scene.RootNodes);
        Assert.Equal(new[] { parent, child }, scene.GetAllNodes().ToArray());
        Assert.Equal(new[] { parent, child }, scene.FindByTag("enemy").ToArray());
    }

    [Fact]
    public void OnNodeParentChanged_RemovesAndRestoresRoots()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = scene.CreateNode("Child");

        scene.OnNodeParentChanged(child, null, parent);
        Assert.DoesNotContain(child, scene.RootNodes);
        Assert.Contains(parent, scene.RootNodes);

        scene.OnNodeParentChanged(child, parent, null);
        Assert.Contains(child, scene.RootNodes);
    }

    [Fact]
    public void RegisterNode_TracksExternalNodesWithoutDuplicatingRoots()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var root = new SceneNode(scene.World, "External");
        scene.RegisterNode(root);
        scene.RegisterNode(root);

        var child = new SceneNode(scene.World, "Nested");
        child.SetParent(parent);
        scene.RegisterNode(child);

        Assert.Equal(new[] { parent, root }, scene.RootNodes.ToArray());
        Assert.DoesNotContain(child, scene.RootNodes);
        Assert.Same(child, scene.GetNode(child.Entity));
        Assert.Same(child, scene.FindNode("Nested"));
    }

    [Fact]
    public void FindQueries_ReturnTaggedAndComponentNodes()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = new SceneNode(scene.World, "Child");
        child.SetParent(parent);
        child.Tag = "pickup";
        scene.RegisterNode(child);
        child.AddComponent(new Marker { Value = 4 });

        Assert.Equal(new[] { child }, scene.FindByTag("pickup").ToArray());
        Assert.Equal(new[] { child }, scene.FindWithComponent<Marker>().ToArray());
        Assert.Empty(scene.FindByTag("missing"));
        Assert.Empty(scene.FindWithComponent<ProbeComponent>());
    }

    [Fact]
    public void GetActiveNodes_SkipsInactiveRootsAndHiddenDescendants()
    {
        var scene = new Scene();
        var activeRoot = scene.CreateNode("Active");
        var visible = new SceneNode(scene.World, "Visible");
        var hidden = new SceneNode(scene.World, "Hidden");
        var buried = new SceneNode(scene.World, "Buried");
        visible.SetParent(activeRoot);
        hidden.SetParent(activeRoot);
        buried.SetParent(hidden);
        scene.RegisterNode(visible);
        scene.RegisterNode(hidden);
        scene.RegisterNode(buried);
        hidden.Active = false;
        buried.Active = true;

        var inactiveRoot = scene.CreateNode("Inactive");
        var orphan = new SceneNode(scene.World, "Orphan");
        orphan.SetParent(inactiveRoot);
        scene.RegisterNode(orphan);
        inactiveRoot.Active = false;
        orphan.Active = true;

        Assert.Equal(new[] { activeRoot, visible }, scene.GetActiveNodes().ToArray());
    }

    [Fact]
    public void UpdateAndFixedUpdate_ReachWorldSystems()
    {
        var scene = new Scene();
        var probe = scene.World.AddSystem(new ProbeSystem());

        scene.Update(0.016f);
        scene.FixedUpdate(0.02f);

        Assert.Equal(1, probe.Updates);
        Assert.Equal(1, probe.FixedUpdates);
    }

    [Fact]
    public void DestroyNode_LeavesDescendantsRegistered()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = new SceneNode(scene.World, "Child");
        child.SetParent(parent);
        scene.RegisterNode(child);
        var childEntity = child.Entity;

        scene.DestroyNode(parent);

        Assert.False(scene.World.IsAlive(parent.Entity));
        Assert.False(scene.World.IsAlive(childEntity));
        Assert.DoesNotContain(parent, scene.RootNodes);
        Assert.Null(scene.GetNode(childEntity));
    }

    [Fact]
    public void DestroyNode_UnregistersDescendants()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = new SceneNode(scene.World, "Child");
        child.SetParent(parent);
        scene.RegisterNode(child);

        scene.DestroyNode(parent);

        Assert.Null(scene.GetNode(child.Entity));
        Assert.DoesNotContain(child, scene.RootNodes);
    }

    [Fact]
    public void DestroyNode_ParentedCreate_LeavesDeadChildInRoots()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = scene.CreateNode(parent, "Child");

        scene.DestroyNode(parent);

        Assert.DoesNotContain(child, scene.RootNodes);
        Assert.False(scene.World.IsAlive(child.Entity));
        Assert.Null(scene.GetNode(child.Entity));
    }

    [Fact]
    public void Clear_DestroysNodesAndEmptiesLookups()
    {
        var scene = new Scene();
        var parent = scene.CreateNode("Parent");
        var child = scene.CreateNode(parent, "Child");
        var childEntity = child.Entity;

        scene.Clear();

        Assert.Empty(scene.RootNodes);
        Assert.Empty(scene.GetAllNodes());
        Assert.Null(scene.GetNode(childEntity));
        Assert.False(scene.World.IsAlive(parent.Entity));
        Assert.False(scene.World.IsAlive(childEntity));
        Assert.Equal(0, scene.World.EntityCount);

        var rebuilt = scene.CreateNode("After");
        Assert.Same(rebuilt, scene.FindNode("After"));
    }

    [Fact]
    public void Dispose_ClearsNodesAndSystems()
    {
        var scene = new Scene();
        scene.CreateNode("Node");
        scene.World.AddSystem(new ProbeSystem());

        scene.Dispose();

        Assert.Empty(scene.RootNodes);
        Assert.Empty(scene.World.Systems);
    }

    private struct ProbeComponent : IComponent
    {
        public int Value;
    }
}
