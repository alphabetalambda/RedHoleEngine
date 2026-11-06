using System.Numerics;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;

namespace RedHoleEngine.Tests.Core.Scene;

public class SceneNodeTests
{
    private struct Marker : IComponent
    {
        public int Value;
    }

    [Fact]
    public void Constructor_CreatesEntityAndIdentityTransform()
    {
        using var world = new World();
        var before = world.EntityCount;
        var node = new SceneNode(world, "Camera");

        Assert.Equal("Camera", node.Name);
        Assert.True(node.Active);
        Assert.Null(node.Tag);
        Assert.Null(node.Parent);
        Assert.Empty(node.Children);
        Assert.Equal(before + 1, world.EntityCount);
        Assert.True(world.IsAlive(node.Entity));
        Assert.Equal(Vector3.Zero, node.Transform.LocalPosition);
        Assert.Equal(Vector3.One, node.Transform.LocalScale);
        Assert.Contains("Camera", node.ToString());
        Assert.Contains(node.Entity.ToString(), node.ToString());
    }

    [Fact]
    public void Constructor_ExistingEntity_DoesNotCreateAnother()
    {
        using var world = new World();
        var entity = world.CreateEntity();

        var node = new SceneNode(world, entity, "Linked");

        Assert.Equal(entity, node.Entity);
        Assert.Equal(1, world.EntityCount);
        Assert.Equal("Linked", node.Name);
    }

    [Fact]
    public void SetParent_BuildsHierarchyAndPreservesWorldPosition()
    {
        using var world = new World();
        var parent = new SceneNode(world, "Parent");
        var child = new SceneNode(world, "Child");
        var grandchild = new SceneNode(world, "Grandchild");
        parent.Transform.Position = new Vector3(10f, 0f, 0f);
        child.Transform.Position = new Vector3(1f, 2f, 3f);

        parent.AddChild(child);
        child.AddChild(grandchild);

        Assert.Same(parent, child.Parent);
        Assert.Contains(child, parent.Children);
        Assert.Same(parent, grandchild.GetRoot());
        Assert.True(parent.IsAncestorOf(grandchild));
        Assert.False(grandchild.IsAncestorOf(parent));
        Assert.False(parent.IsAncestorOf(parent));
        Assert.Equal(1f, child.Transform.Position.X, 3);
        Assert.Equal(2f, child.Transform.Position.Y, 3);
        Assert.Equal(3f, child.Transform.Position.Z, 3);
        Assert.Equal(-9f, child.Transform.LocalPosition.X, 3);
    }

    [Fact]
    public void SetParent_WorldPositionStaysFalse_KeepsLocalPosition()
    {
        using var world = new World();
        var parent = new SceneNode(world, "Parent");
        var child = new SceneNode(world, "Child");
        parent.Transform.Position = new Vector3(10f, 0f, 0f);
        child.Transform.LocalPosition = new Vector3(1f, 2f, 3f);

        child.SetParent(parent, worldPositionStays: false);

        Assert.Equal(new Vector3(1f, 2f, 3f), child.Transform.LocalPosition);
        Assert.Equal(11f, child.Transform.Position.X, 3);
        Assert.Equal(2f, child.Transform.Position.Y, 3);
        Assert.Equal(3f, child.Transform.Position.Z, 3);
    }

    [Fact]
    public void SetParent_SameParent_DoesNotDuplicateChild()
    {
        using var world = new World();
        var parent = new SceneNode(world, "Parent");
        var child = new SceneNode(world, "Child");
        parent.AddChild(child);

        child.SetParent(parent);

        Assert.Equal(1, parent.Children.Count);
        Assert.Same(parent, child.Parent);
    }

    [Fact]
    public void SetParent_NullAndRemoveChild_DetachOnlyTheMatchingChild()
    {
        using var world = new World();
        var parent = new SceneNode(world, "Parent");
        var child = new SceneNode(world, "Child");
        var other = new SceneNode(world, "Other");
        parent.AddChild(child);

        parent.RemoveChild(other);
        Assert.Same(parent, child.Parent);

        parent.RemoveChild(child);
        Assert.Null(child.Parent);
        Assert.Empty(parent.Children);

        child.SetParent(null);
        Assert.Null(child.Parent);
    }

    [Fact]
    public void SetParent_Descendant_Throws()
    {
        using var world = new World();
        var root = new SceneNode(world, "Root");
        var middle = new SceneNode(world, "Middle");
        var leaf = new SceneNode(world, "Leaf");
        root.AddChild(middle);
        middle.AddChild(leaf);

        var ex = Assert.Throws<InvalidOperationException>(() => root.SetParent(leaf));
        Assert.Contains("circular", ex.Message);
        Assert.Null(root.Parent);
        Assert.Same(root, middle.Parent);
    }

    [Fact]
    public void SetParent_Self_CreatesACycle()
    {
        using var world = new World();
        var node = new SceneNode(world, "Loop");

        var ex = Assert.Throws<InvalidOperationException>(() => node.SetParent(node));
        Assert.Contains("circular", ex.Message);
        Assert.Null(node.Parent);
    }

    [Fact]
    public void SetParent_Self_Throws()
    {
        using var world = new World();
        var node = new SceneNode(world, "Loop");

        Assert.Throws<InvalidOperationException>(() => node.SetParent(node));
    }

    [Fact]
    public void ComponentAccess_AddGetRemove()
    {
        using var world = new World();
        var node = new SceneNode(world, "Node");

        Assert.False(node.HasComponent<Marker>());
        Assert.False(node.TryGetComponent<Marker>(out _));
        Assert.Throws<KeyNotFoundException>(() => node.GetComponent<Marker>());

        ref var marker = ref node.AddComponent(new Marker { Value = 7 });
        marker.Value = 9;

        Assert.True(node.HasComponent<Marker>());
        Assert.True(node.TryGetComponent<Marker>(out var copy));
        Assert.Equal(9, copy.Value);
        Assert.Equal(9, node.GetComponent<Marker>().Value);

        node.RemoveComponent<Marker>();
        Assert.False(node.HasComponent<Marker>());
    }

    [Fact]
    public void Queries_SearchDirectChildrenAndDescendants()
    {
        using var world = new World();
        var root = new SceneNode(world, "Root");
        var child = new SceneNode(world, "Child");
        var grandchild = new SceneNode(world, "Grandchild");
        var hidden = new SceneNode(world, "Hidden");
        root.AddChild(child);
        child.AddChild(grandchild);
        child.AddChild(hidden);
        root.Tag = "root";
        grandchild.Tag = "goal";
        grandchild.AddComponent(new Marker { Value = 1 });
        hidden.Active = false;
        hidden.Tag = "goal";

        Assert.Same(child, root.FindChild("Child"));
        Assert.Null(root.FindChild("Grandchild"));
        Assert.Null(root.FindChild("Missing"));
        Assert.Same(grandchild, root.FindDescendant("Grandchild"));
        Assert.Null(root.FindDescendant("Root"));
        Assert.Equal(new[] { child, grandchild, hidden }, root.GetDescendants().ToArray());
        Assert.Equal(new[] { grandchild, hidden }, root.FindByTag("goal").ToArray());
        Assert.Equal(new[] { root }, root.FindByTag("root").ToArray());
        Assert.Equal(new[] { grandchild }, root.FindWithComponent<Marker>().ToArray());
        Assert.Equal(new[] { child, grandchild }, root.GetActiveDescendants().ToArray());
        Assert.Empty(hidden.GetActiveDescendants());
    }

    [Fact]
    public void Destroy_DestroysChildrenAndDetachesFromParent()
    {
        using var world = new World();
        var parent = new SceneNode(world, "Parent");
        var child = new SceneNode(world, "Child");
        var grandchild = new SceneNode(world, "Grandchild");
        parent.AddChild(child);
        child.AddChild(grandchild);

        child.Destroy();

        Assert.Empty(parent.Children);
        Assert.False(world.IsAlive(child.Entity));
        Assert.False(world.IsAlive(grandchild.Entity));
        Assert.True(world.IsAlive(parent.Entity));
        Assert.Throws<InvalidOperationException>(() => child.AddComponent(new Marker()));
    }
}
